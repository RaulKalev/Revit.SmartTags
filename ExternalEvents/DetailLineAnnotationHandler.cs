using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using SmartTags.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SmartTags.ExternalEvents
{
    public class DetailLineAnnotationHandler : IExternalEventHandler
    {
        public ElementId DetailItemTypeId { get; set; } = ElementId.InvalidElementId;
        public double OffsetMillimeters { get; set; } = 0;
        public PlacementDirection OffsetDirection { get; set; } = PlacementDirection.Right;
        public bool AlignToLineDirection { get; set; }
        public bool UseSelection { get; set; }
        public bool UseWireElements { get; set; }
        public IList<ElementId> TargetElementIds { get; set; }

        public int LastPlacedCount { get; private set; }

        public bool DiagnosticMode { get; set; }

        public void Execute(UIApplication app)
        {
            LastPlacedCount = 0;

            var uiDoc = app.ActiveUIDocument;
            if (uiDoc == null)
            {
                return;
            }

            var doc = uiDoc.Document;
            var view = doc.ActiveView;
            if (view == null || DetailItemTypeId == ElementId.InvalidElementId)
            {
                return;
            }

            var symbol = doc.GetElement(DetailItemTypeId) as FamilySymbol;
            if (symbol == null)
            {
                TaskDialog.Show("SmartTags", "The selected detail item type is no longer available in the document.");
                return;
            }

            using (var t = new Transaction(doc, "SmartTags: Annotate Detail Lines"))
            {
                t.Start();

                if (!symbol.IsActive)
                {
                    symbol.Activate();
                    doc.Regenerate();
                }

                var viewDirection = view.ViewDirection;
                var viewAxis = viewDirection != null && viewDirection.GetLength() > 1e-9
                    ? viewDirection.Normalize()
                    : XYZ.BasisZ;

                var scaleFactor = Math.Max(1, view.Scale);
                var offsetInFeet = OffsetMillimeters / 304.8;

                int placedCount = 0;

                if (UseWireElements)
                {
                    IEnumerable<Wire> wires;
                    if (UseSelection && TargetElementIds != null && TargetElementIds.Count > 0)
                    {
                        wires = TargetElementIds
                            .Select(id => doc.GetElement(id))
                            .OfType<Wire>();
                    }
                    else
                    {
                        wires = new FilteredElementCollector(doc, view.Id)
                            .OfClass(typeof(Wire))
                            .Cast<Wire>();
                    }

                    foreach (var wire in wires)
                    {
                        if (DiagnosticMode)
                        {
                            t.RollBack();
                            ShowWireDiagnostic(wire, view);
                            return;
                        }

                        // Get the wire's visual path as a polyline.
                        // Wire.LocationCurve is just the chord; the real path is
                        // stored as a PolyLine in the element's geometry.
                        var pathPoints = GetWirePathPoints(wire, view);
                        if (pathPoints == null || pathPoints.Count < 2)
                            continue;

                        var midpoint = GetMidpointAlongPoints(pathPoints);
                        if (midpoint == null)
                            continue;

                        var placementPoint = midpoint;
                        if (offsetInFeet * scaleFactor > 1e-9)
                        {
                            var directionVector = GetDirectionVector(view, OffsetDirection);
                            placementPoint = midpoint + directionVector.Multiply(offsetInFeet * scaleFactor);
                        }

                        FamilyInstance instance;
                        try
                        {
                            instance = doc.Create.NewFamilyInstance(placementPoint, symbol, view);
                        }
                        catch
                        {
                            continue;
                        }

                        if (instance == null)
                            continue;

                        if (AlignToLineDirection)
                        {
                            try
                            {
                                var angle = GetRotationAngleFromPoints(pathPoints, midpoint, view);
                                if (Math.Abs(angle) > 1e-9)
                                {
                                    var axis = Line.CreateBound(placementPoint, placementPoint + viewAxis);
                                    ElementTransformUtils.RotateElement(doc, instance.Id, axis, angle);
                                }
                            }
                            catch
                            {
                                // Rotation failure does not prevent placement
                            }
                        }

                        placedCount++;
                    }
                }
                else
                {
                    IEnumerable<DetailCurve> detailCurves;
                    if (UseSelection && TargetElementIds != null && TargetElementIds.Count > 0)
                    {
                        detailCurves = TargetElementIds
                            .Select(id => doc.GetElement(id))
                            .OfType<DetailCurve>();
                    }
                    else
                    {
                        detailCurves = new FilteredElementCollector(doc, view.Id)
                            .OfClass(typeof(DetailCurve))
                            .Cast<DetailCurve>();
                    }

                    foreach (var curve in detailCurves.Select(dc => dc.GeometryCurve).Where(c => c != null))
                    {
                        var midpoint = DetailLineCenterService.GetMidpoint(curve);
                        if (midpoint == null)
                            continue;

                        var placementPoint = midpoint;
                        if (offsetInFeet * scaleFactor > 1e-9)
                        {
                            var directionVector = GetDirectionVector(view, OffsetDirection);
                            placementPoint = midpoint + directionVector.Multiply(offsetInFeet * scaleFactor);
                        }

                        FamilyInstance instance;
                        try
                        {
                            instance = doc.Create.NewFamilyInstance(placementPoint, symbol, view);
                        }
                        catch
                        {
                            continue;
                        }

                        if (instance == null)
                            continue;

                        if (AlignToLineDirection)
                        {
                            try
                            {
                                var angle = DetailLineCenterService.GetRotationAngle(curve, view);
                                if (Math.Abs(angle) > 1e-9)
                                {
                                    var axis = Line.CreateBound(placementPoint, placementPoint + viewAxis);
                                    ElementTransformUtils.RotateElement(doc, instance.Id, axis, angle);
                                }
                            }
                            catch
                            {
                                // Rotation failure does not prevent placement
                            }
                        }

                        placedCount++;
                    }
                }

                t.Commit();

                LastPlacedCount = placedCount;

                if (placedCount == 0 && !UseSelection)
                {
                    var elementTypeName = UseWireElements ? "wires" : "detail lines";
                    TaskDialog.Show("SmartTags", "No " + elementTypeName + " were found in the active view.");
                }
            }
        }

        public string GetName()
        {
            return "SmartTags Detail Line Annotation";
        }

        private static void ShowWireDiagnostic(Wire wire, View view)
        {
            var sb = new System.Text.StringBuilder();

            // LocationCurve
            var locCurve = (wire.Location as LocationCurve)?.Curve;
            sb.AppendLine("LocationCurve type: " + (locCurve?.GetType().Name ?? "null"));
            if (locCurve != null)
            {
                try { sb.AppendLine("  Tessellate count: " + locCurve.Tessellate().Count); } catch (Exception ex) { sb.AppendLine("  Tessellate threw: " + ex.Message); }
                try { sb.AppendLine("  Evaluate(0.5): " + locCurve.Evaluate(0.5, true)); } catch (Exception ex) { sb.AppendLine("  Evaluate threw: " + ex.Message); }
            }

            // Connectors
            try
            {
                var mgr = wire.ConnectorManager;
                if (mgr != null)
                {
                    var conns = mgr.Connectors.Cast<Connector>().ToList();
                    sb.AppendLine("Connectors: " + conns.Count);
                    foreach (var c in conns)
                        sb.AppendLine("  " + c.Origin);
                }
            }
            catch (Exception ex) { sb.AppendLine("Connectors threw: " + ex.Message); }

            // Geometry without view
            try
            {
                sb.AppendLine("--- Geometry (no view) ---");
                DescribeGeometry(wire.get_Geometry(new Options { ComputeReferences = false }), sb, "  ");
            }
            catch (Exception ex) { sb.AppendLine("Geom(no view) threw: " + ex.Message); }

            // Geometry with view
            try
            {
                sb.AppendLine("--- Geometry (with view) ---");
                DescribeGeometry(wire.get_Geometry(new Options { View = view, ComputeReferences = false }), sb, "  ");
            }
            catch (Exception ex) { sb.AppendLine("Geom(view) threw: " + ex.Message); }

            TaskDialog.Show("Wire Diagnostic", sb.ToString());
        }

        private static void DescribeGeometry(GeometryElement geom, System.Text.StringBuilder sb, string indent)
        {
            if (geom == null) { sb.AppendLine(indent + "(null)"); return; }
            foreach (var obj in geom)
            {
                if (obj is Curve c)
                {
                    try { sb.AppendLine(indent + c.GetType().Name + " ep0=" + c.GetEndPoint(0) + " ep1=" + c.GetEndPoint(1) + " tessCount=" + c.Tessellate().Count); }
                    catch { sb.AppendLine(indent + c.GetType().Name + " (endpoint access failed)"); }
                }
                else if (obj is PolyLine pl)
                {
                    var pts = pl.GetCoordinates();
                    sb.AppendLine(indent + "PolyLine pts=" + (pts?.Count ?? 0) + (pts?.Count > 0 ? " first=" + pts[0] + " last=" + pts[pts.Count - 1] : ""));
                }
                else if (obj is GeometryInstance gi)
                {
                    sb.AppendLine(indent + "GeometryInstance:");
                    DescribeGeometry(gi.GetInstanceGeometry(), sb, indent + "  ");
                }
                else
                {
                    sb.AppendLine(indent + obj.GetType().Name);
                }
            }
        }

        // Returns the wire's visual path as ordered XYZ points.
        //
        // Revit wires store their two physical endpoints in the ConnectorManager.
        // The geometry element contains the actual arc/spline path (as one or more
        // Curve objects or a PolyLine) mixed with annotation tick marks.
        // We identify the wire path by finding the geometry object whose endpoints
        // match the connector positions within a loose tolerance.
        private static List<XYZ> GetWirePathPoints(Wire wire, View view)
        {
            if (wire == null)
                return null;

            // 1. Get the wire's physical endpoint positions from its connectors.
            XYZ connA = null, connB = null;
            try
            {
                var mgr = wire.ConnectorManager;
                if (mgr != null)
                {
                    var list = mgr.Connectors.Cast<Connector>().ToList();
                    if (list.Count >= 1) connA = list[0].Origin;
                    if (list.Count >= 2) connB = list[1].Origin;
                }
            }
            catch { }

            // 2. Collect all curves and polylines from element geometry.
            var geomCurves = new List<Curve>();
            var geomPolyLines = new List<List<XYZ>>();

            foreach (var useView in new[] { false, true })
            {
                if (geomCurves.Count > 0 || geomPolyLines.Count > 0) break;
                try
                {
                    Options opts;
                    if (useView && view != null)
                        opts = new Options { View = view, ComputeReferences = false };
                    else
                        opts = new Options { ComputeReferences = false };
                    CollectGeometry(wire.get_Geometry(opts), geomCurves, geomPolyLines);
                }
                catch { }
            }

            // 3. If we have connector positions, find the geometry that spans them.
            //    This identifies the wire path regardless of annotation marks.
            if (connA != null && connB != null)
            {
                const double tol = 1.0; // 1 foot — generous; connectors may not be exactly on path ends

                foreach (var c in geomCurves)
                {
                    try
                    {
                        var p0 = c.GetEndPoint(0);
                        var p1 = c.GetEndPoint(1);
                        if ((p0.DistanceTo(connA) < tol && p1.DistanceTo(connB) < tol) ||
                            (p0.DistanceTo(connB) < tol && p1.DistanceTo(connA) < tol))
                        {
                            return new List<XYZ>(c.Tessellate());
                        }
                    }
                    catch { }
                }

                foreach (var pl in geomPolyLines)
                {
                    if (pl.Count < 2) continue;
                    var p0 = pl[0];
                    var p1 = pl[pl.Count - 1];
                    if ((p0.DistanceTo(connA) < tol && p1.DistanceTo(connB) < tol) ||
                        (p0.DistanceTo(connB) < tol && p1.DistanceTo(connA) < tol))
                        return pl;
                }
            }

            // 4. No connector match — try LocationCurve directly.
            //    For Arc/NurbSpline wires this gives the real curved path.
            var locCurve = (wire.Location as LocationCurve)?.Curve;
            if (locCurve != null)
            {
                try
                {
                    var tess = locCurve.Tessellate();
                    if (tess != null && tess.Count >= 2)
                        return new List<XYZ>(tess);
                }
                catch { }
            }

            // 5. Last resort: pick the geometry curve/polyline with the greatest
            //    span (straight-line distance end-to-end) — most likely the path.
            double bestSpan = -1;
            List<XYZ> bestPts = null;
            foreach (var c in geomCurves)
            {
                try
                {
                    var span = c.GetEndPoint(0).DistanceTo(c.GetEndPoint(1));
                    if (span > bestSpan) { bestSpan = span; bestPts = new List<XYZ>(c.Tessellate()); }
                }
                catch { }
            }
            foreach (var pl in geomPolyLines)
            {
                if (pl.Count < 2) continue;
                var span = pl[0].DistanceTo(pl[pl.Count - 1]);
                if (span > bestSpan) { bestSpan = span; bestPts = pl; }
            }
            if (bestPts != null)
                return bestPts;

            return null;
        }

        private static void CollectGeometry(GeometryElement geom, List<Curve> curves, List<List<XYZ>> polylines)
        {
            if (geom == null) return;
            foreach (var obj in geom)
            {
                if (obj is Curve c)
                {
                    curves.Add(c);
                }
                else if (obj is PolyLine pl)
                {
                    var pts = pl.GetCoordinates();
                    if (pts != null && pts.Count >= 2)
                        polylines.Add(new List<XYZ>(pts));
                }
                else if (obj is GeometryInstance gi)
                {
                    CollectGeometry(gi.GetInstanceGeometry(), curves, polylines);
                }
            }
        }

        // Arc-length midpoint across a polyline defined by ordered XYZ vertices.
        private static XYZ GetMidpointAlongPoints(List<XYZ> points)
        {
            if (points == null || points.Count == 0)
                return null;
            if (points.Count == 1)
                return points[0];

            double total = 0;
            for (int i = 0; i < points.Count - 1; i++)
                total += points[i].DistanceTo(points[i + 1]);

            if (total < 1e-9)
                return points[0];

            double half = total / 2.0;
            double accumulated = 0;
            for (int i = 0; i < points.Count - 1; i++)
            {
                double len = points[i].DistanceTo(points[i + 1]);
                if (accumulated + len >= half - 1e-9)
                {
                    double t = len > 1e-9 ? (half - accumulated) / len : 0.0;
                    return points[i] + (points[i + 1] - points[i]).Multiply(t);
                }
                accumulated += len;
            }

            return points[points.Count - 1];
        }

        // Rotation angle of the polyline segment closest to nearPoint, projected onto the view plane.
        private static double GetRotationAngleFromPoints(List<XYZ> points, XYZ nearPoint, View view)
        {
            if (points == null || points.Count < 2 || view == null)
                return 0;

            int midIdx = 0;
            double minDist = double.MaxValue;
            for (int i = 0; i < points.Count - 1; i++)
            {
                var segMid = new XYZ(
                    (points[i].X + points[i + 1].X) * 0.5,
                    (points[i].Y + points[i + 1].Y) * 0.5,
                    (points[i].Z + points[i + 1].Z) * 0.5);
                double d = segMid.DistanceTo(nearPoint);
                if (d < minDist) { minDist = d; midIdx = i; }
            }

            var dir = points[midIdx + 1] - points[midIdx];
            if (dir.GetLength() < 1e-9)
                return 0;

            var normal = view.ViewDirection;
            var projected = dir - normal.Multiply(dir.DotProduct(normal));
            if (projected.GetLength() < 1e-9)
                return 0;

            var tangent = projected.Normalize();
            var right = view.RightDirection;
            var unsignedAngle = right.AngleTo(tangent);
            var cross = right.CrossProduct(tangent);
            return cross.DotProduct(normal) < 0 ? -unsignedAngle : unsignedAngle;
        }

        private static XYZ GetDirectionVector(View view, PlacementDirection direction)
        {
            if (view == null)
            {
                return XYZ.BasisX;
            }

            var right = view.RightDirection;
            var up = view.UpDirection;

            switch (direction)
            {
                case PlacementDirection.Up:
                    return up;
                case PlacementDirection.Down:
                    return up.Negate();
                case PlacementDirection.Left:
                    return right.Negate();
                case PlacementDirection.Right:
                default:
                    return right;
            }
        }
    }
}
