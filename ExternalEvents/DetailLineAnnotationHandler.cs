using Autodesk.Revit.DB;
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
        public IList<ElementId> TargetElementIds { get; set; }

        public int LastPlacedCount { get; private set; }

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

                var viewDirection = view.ViewDirection;
                var viewAxis = viewDirection != null && viewDirection.GetLength() > 1e-9
                    ? viewDirection.Normalize()
                    : XYZ.BasisZ;

                var scaleFactor = Math.Max(1, view.Scale);
                var offsetInFeet = OffsetMillimeters / 304.8;

                int placedCount = 0;
                foreach (var detailCurve in detailCurves)
                {
                    var midpoint = DetailLineCenterService.GetMidpoint(detailCurve);
                    if (midpoint == null)
                    {
                        continue;
                    }

                    var placementPoint = midpoint;

                    // Apply offset in the chosen view direction, scaled by view scale
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
                    {
                        continue;
                    }

                    if (AlignToLineDirection)
                    {
                        try
                        {
                            var angle = DetailLineCenterService.GetRotationAngle(detailCurve, view);
                            if (Math.Abs(angle) > 1e-9)
                            {
                                var axis = Line.CreateBound(
                                    placementPoint,
                                    placementPoint + viewAxis);
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

                t.Commit();

                LastPlacedCount = placedCount;

                if (placedCount == 0 && !UseSelection)
                {
                    TaskDialog.Show("SmartTags", "No detail lines were found in the active view.");
                }
            }
        }

        public string GetName()
        {
            return "SmartTags Detail Line Annotation";
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
