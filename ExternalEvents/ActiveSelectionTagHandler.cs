using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SmartTags.Services;
using System;

namespace SmartTags.ExternalEvents
{
    public class ActiveSelectionTagHandler : IExternalEventHandler
    {
        public ElementId ElementToTag { get; set; }
        public ElementId CategoryId { get; set; }
        public ElementId TagTypeId { get; set; }
        public ElementId TagCategoryId { get; set; }
        public bool HasLeader { get; set; }
        public double AttachedLength { get; set; }
        public double FreeLength { get; set; }
        public TagOrientation Orientation { get; set; }
        public double Angle { get; set; }
        public PlacementDirection Direction { get; set; }
        public bool DetectElementRotation { get; set; }
        public bool EnableCollisionDetection { get; set; }
        public double CollisionGapMillimeters { get; set; }
        public double MinimumOffsetMillimeters { get; set; }
        public bool SkipIfAlreadyTagged { get; set; }

        public bool Success { get; private set; }
        public string Message { get; private set; }

        public void Execute(UIApplication app)
        {
            Success = false;
            Message = string.Empty;

            var uiDoc = app.ActiveUIDocument;
            if (uiDoc == null)
            {
                Message = "No active document.";
                return;
            }

            var doc = uiDoc.Document;
            var view = doc.ActiveView;
            if (view == null)
            {
                Message = "No active view.";
                return;
            }

            if (ElementToTag == null || ElementToTag == ElementId.InvalidElementId)
            {
                Message = "Invalid element.";
                return;
            }

            var element = doc.GetElement(ElementToTag);
            if (element == null)
            {
                Message = "Element not found.";
                return;
            }

            if (SkipIfAlreadyTagged)
            {
                if (TagExistenceChecker.IsElementTaggedInView(doc, view, ElementToTag, TagCategoryId))
                {
                    Message = "Element already tagged (skipped).";
                    Success = true;
                    return;
                }
            }

            var tagSymbol = doc.GetElement(TagTypeId) as FamilySymbol;

            using (var transaction = new Transaction(doc, "SmartTags: Active Selection"))
            {
                transaction.Start();

                try
                {
                    if (tagSymbol != null && !tagSymbol.IsActive)
                    {
                        tagSymbol.Activate();
                        doc.Regenerate();
                    }

                    if (!TryGetAnchorPoint(element, view, out var anchor))
                    {
                        Message = "Could not determine anchor point.";
                        return;
                    }

                    var directionVector = GetDirectionVector(view, Direction);
                    var viewDirection = view.ViewDirection;
                    var viewAxis = viewDirection != null && viewDirection.GetLength() > 1e-9
                        ? viewDirection.Normalize()
                        : XYZ.BasisZ;
                    var scaleFactor = Math.Max(1, view.Scale);
                    var attachedLength = AttachedLength * scaleFactor;
                    var freeLength = FreeLength * scaleFactor;

                    var totalAngle = Angle;
                    if (DetectElementRotation && TryGetElementRotationAngle(element, view, out var elementAngle))
                    {
                        totalAngle += elementAngle;
                    }

                    var offsetDirection = RotateVectorAroundAxis(directionVector, viewAxis, totalAngle);
                    var head = anchor;
                    var leaderOffset = Math.Max(0, attachedLength + freeLength);

                    if (HasLeader && leaderOffset > 0)
                    {
                        head = anchor + offsetDirection.Multiply(leaderOffset);
                    }
                    else if (!HasLeader)
                    {
                        var minimumOffset = MinimumOffsetMillimeters / 304.8;
                        head = anchor + offsetDirection.Multiply(minimumOffset);
                    }
                    else if (freeLength > 0)
                    {
                        head = anchor + offsetDirection.Multiply(freeLength);
                    }

                    TagCollisionDetector collisionDetector = null;
                    if (EnableCollisionDetection)
                    {
                        collisionDetector = new TagCollisionDetector(view, CollisionGapMillimeters);
                        collisionDetector.CollectObstacles(doc);

                        bool foundValidPosition;
                        head = collisionDetector.FindValidPosition(anchor, head, out foundValidPosition);
                    }

                    var reference = new Reference(element);
                    bool shouldDisableLeaderAfterCreation = !HasLeader;
                    bool createWithLeader = HasLeader || shouldDisableLeaderAfterCreation;

                    var tag = CreateTag(doc, view, reference, head, Orientation, createWithLeader);
                    if (tag == null)
                    {
                        Message = "Failed to create tag.";
                        return;
                    }

                    try
                    {
                        tag.TagHeadPosition = head;

                        if (shouldDisableLeaderAfterCreation)
                        {
                            tag.HasLeader = false;
                        }
                    }
                    catch
                    {
                    }

                    if (TagTypeId != ElementId.InvalidElementId && tag.GetTypeId() != TagTypeId)
                    {
                        try
                        {
                            tag.ChangeTypeId(TagTypeId);
                        }
                        catch
                        {
                        }
                    }

                    if (Math.Abs(totalAngle) > 1e-9)
                    {
                        try
                        {
                            var axis = Line.CreateBound(head, head + viewDirection);
                            ElementTransformUtils.RotateElement(doc, tag.Id, axis, totalAngle);
                        }
                        catch
                        {
                        }
                    }

                    if (collisionDetector != null && tag != null)
                    {
                        doc.Regenerate();

                        if (collisionDetector.HasCollisionWithActualBounds(tag, out var actualBounds))
                        {
                            bool foundValidPosition;
                            var newHead = collisionDetector.FindValidPositionWithActualSize(anchor, head, actualBounds, out foundValidPosition);

                            if (foundValidPosition && (newHead - head).GetLength() > 1e-6)
                            {
                                try
                                {
                                    tag.TagHeadPosition = newHead;

                                    if (Math.Abs(totalAngle) > 1e-9)
                                    {
                                        var newAxis = Line.CreateBound(newHead, newHead + viewDirection);
                                        ElementTransformUtils.RotateElement(doc, tag.Id, newAxis, totalAngle);
                                    }

                                    head = newHead;
                                    doc.Regenerate();
                                }
                                catch
                                {
                                }
                            }
                        }

                        collisionDetector.AddNewTag(tag);
                    }

                    SmartTagMarkerStorage.SetManagedTag(tag, element.Id);

                    transaction.Commit();
                    Success = true;
                    Message = "Tag created.";
                }
                catch (Exception ex)
                {
                    if (transaction.HasStarted())
                    {
                        transaction.RollBack();
                    }
                    Message = $"Error: {ex.Message}";
                }
            }
        }

        public string GetName()
        {
            return "SmartTags Active Selection Tag";
        }

        private static bool TryGetAnchorPoint(Element element, View view, out XYZ anchor)
        {
            anchor = null;
            if (element == null || view == null)
            {
                return false;
            }

            var bbox = element.get_BoundingBox(view);
            if (bbox != null)
            {
                anchor = (bbox.Min + bbox.Max) * 0.5;
                return true;
            }

            if (element.Location is LocationPoint point)
            {
                anchor = point.Point;
                return true;
            }

            if (element.Location is LocationCurve curve)
            {
                var c = curve.Curve;
                if (c != null)
                {
                    anchor = (c.GetEndPoint(0) + c.GetEndPoint(1)) * 0.5;
                    return true;
                }
            }

            return false;
        }

        private static IndependentTag CreateTag(Document doc, View view, Reference reference, XYZ head, TagOrientation orientation, bool hasLeader)
        {
            try
            {
                return IndependentTag.Create(doc, view.Id, reference, hasLeader, TagMode.TM_ADDBY_CATEGORY, orientation, head);
            }
            catch
            {
                if (orientation != TagOrientation.Horizontal)
                {
                    try
                    {
                        return IndependentTag.Create(doc, view.Id, reference, hasLeader, TagMode.TM_ADDBY_CATEGORY, TagOrientation.Horizontal, head);
                    }
                    catch
                    {
                    }
                }
            }

            return null;
        }

        private static XYZ RotateVectorAroundAxis(XYZ vector, XYZ axis, double angle)
        {
            if (vector == null)
            {
                return null;
            }

            if (axis == null || axis.GetLength() < 1e-9 || Math.Abs(angle) < 1e-9)
            {
                return vector;
            }

            var rotation = Transform.CreateRotationAtPoint(axis.Normalize(), angle, XYZ.Zero);
            return rotation.OfVector(vector);
        }

        private static bool TryGetElementRotationAngle(Element element, View view, out double angle)
        {
            angle = 0;
            if (element == null || view == null)
            {
                return false;
            }

            XYZ direction = null;

            if (element is FamilyInstance familyInstance)
            {
                direction = familyInstance.HandOrientation;
                if (direction == null || direction.GetLength() < 1e-6)
                {
                    direction = familyInstance.FacingOrientation;
                }
            }

            if (direction == null && element.Location is LocationCurve curve)
            {
                var elementCurve = curve.Curve;
                if (elementCurve != null)
                {
                    direction = elementCurve.GetEndPoint(1) - elementCurve.GetEndPoint(0);
                }
            }

            if (direction == null && element.Location is LocationPoint point)
            {
                var axis = view.ViewDirection;
                if (axis != null && axis.GetLength() > 1e-9)
                {
                    var transform = Transform.CreateRotationAtPoint(axis.Normalize(), point.Rotation, XYZ.Zero);
                    direction = transform.OfVector(view.RightDirection);
                }
            }

            if (direction == null || direction.GetLength() < 1e-6)
            {
                return false;
            }

            return TryGetSignedAngleInViewPlane(view, direction, out angle);
        }

        private static bool TryGetSignedAngleInViewPlane(View view, XYZ direction, out double angle)
        {
            angle = 0;
            if (view == null || direction == null)
            {
                return false;
            }

            var normal = view.ViewDirection;
            if (normal == null || normal.GetLength() < 1e-9)
            {
                return false;
            }

            var baseDir = view.RightDirection;
            if (baseDir == null || baseDir.GetLength() < 1e-9)
            {
                return false;
            }

            var directionProj = direction - normal.Multiply(direction.DotProduct(normal));
            var baseProj = baseDir - normal.Multiply(baseDir.DotProduct(normal));

            if (directionProj.GetLength() < 1e-6 || baseProj.GetLength() < 1e-6)
            {
                return false;
            }

            directionProj = directionProj.Normalize();
            baseProj = baseProj.Normalize();

            var unsignedAngle = baseProj.AngleTo(directionProj);
            var cross = baseProj.CrossProduct(directionProj);
            var sign = cross.DotProduct(normal) < 0 ? -1.0 : 1.0;
            angle = unsignedAngle * sign;
            return true;
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
