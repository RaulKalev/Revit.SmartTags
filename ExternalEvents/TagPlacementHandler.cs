using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SmartTags.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SmartTags.ExternalEvents
{
    public class TagPlacementHandler : IExternalEventHandler
    {
        public ElementId CategoryId { get; set; } = ElementId.InvalidElementId;
        public ElementId TagTypeId { get; set; } = ElementId.InvalidElementId;
        public DirectionTagTypeResolver DirectionResolver { get; set; }
        public bool HasLeader { get; set; }
        public double AttachedLength { get; set; }
        public double FreeLength { get; set; }
        public TagOrientation Orientation { get; set; } = TagOrientation.Horizontal;
        public double Angle { get; set; }
        public PlacementDirection Direction { get; set; } = PlacementDirection.Right;
        public bool DetectElementRotation { get; set; }
        public bool UseSelection { get; set; }
        public IList<ElementId> TargetElementIds { get; set; }
        public bool EnableCollisionDetection { get; set; }
        public double CollisionGapMillimeters { get; set; } = 1.0;
        public double MinimumOffsetMillimeters { get; set; } = 300.0;

        public void Execute(UIApplication app)
        {
            var uiDoc = app.ActiveUIDocument;
            if (uiDoc == null)
            {
                return;
            }

            var doc = uiDoc.Document;
            var view = doc.ActiveView;
            if (view == null || CategoryId == ElementId.InvalidElementId)
            {
                return;
            }

            ElementId resolvedTagTypeId = TagTypeId;
            bool usingDirectionOverride = false;
            if (DirectionResolver != null)
            {
                ElementId directionSpecificTypeId = DirectionResolver.ResolveTagTypeForDirection(Direction);
                if (directionSpecificTypeId != ElementId.InvalidElementId && directionSpecificTypeId != TagTypeId)
                {
                    resolvedTagTypeId = directionSpecificTypeId;
                    usingDirectionOverride = true;
                }
            }

            var tagSymbol = doc.GetElement(resolvedTagTypeId) as FamilySymbol;
            var transactionName = UseSelection ? "SmartTags: Tag Selected" : "SmartTags: Tag All";
            using (var t = new Transaction(doc, transactionName))
            {
                t.Start();

                if (tagSymbol != null && !tagSymbol.IsActive)
                {
                    tagSymbol.Activate();
                    doc.Regenerate();
                }

                IEnumerable<Element> elements;
                if (UseSelection && TargetElementIds != null && TargetElementIds.Count > 0)
                {
                    elements = TargetElementIds
                        .Select(id => doc.GetElement(id))
                        .Where(element => element != null && element.Category != null && element.Category.Id == CategoryId);
                }
                else
                {
                    elements = new FilteredElementCollector(doc, view.Id)
                        .OfCategoryId(CategoryId)
                        .WhereElementIsNotElementType()
                        .ToElements();
                }

                var directionVector = GetDirectionVector(view, Direction);
                var viewDirection = view.ViewDirection;
                var viewAxis = viewDirection != null && viewDirection.GetLength() > 1e-9
                    ? viewDirection.Normalize()
                    : XYZ.BasisZ;
                var scaleFactor = Math.Max(1, view.Scale);
                var attachedLength = AttachedLength * scaleFactor;
                var freeLength = FreeLength * scaleFactor;

                // Initialize collision detector once for all elements if enabled
                TagCollisionDetector collisionDetector = null;
                if (EnableCollisionDetection)
                {
                    collisionDetector = new TagCollisionDetector(view, CollisionGapMillimeters);
                }

                int taggedCount = 0;
                int collisionCount = 0;
                foreach (var element in elements)
                {
                    if (!TryGetAnchorPoint(element, view, out var anchor))
                    {
                        continue;
                    }

                    // Update obstacles for this specific element (excludes current element but keeps track of newly created tags)
                    if (collisionDetector != null)
                    {
                        collisionDetector.CollectObstacles(doc, element.Id);
                    }

                    var totalAngle = Angle;
                    if (DetectElementRotation && TryGetElementRotationAngle(element, view, out var elementAngle))
                    {
                        totalAngle += elementAngle;
                    }

                    var offsetDirection = directionVector;
                    if (usingDirectionOverride)
                    {
                        if (DetectElementRotation && TryGetElementRotationAngle(element, view, out var elemRot))
                        {
                            offsetDirection = RotateVectorAroundAxis(directionVector, viewAxis, elemRot);
                        }
                    }
                    else
                    {
                        offsetDirection = RotateVectorAroundAxis(directionVector, viewAxis, totalAngle);
                    }
                    var head = anchor;
                    var leaderOffset = Math.Max(0, attachedLength + freeLength);

                    if (HasLeader && leaderOffset > 0)
                    {
                        // Leader enabled with length specified
                        head = anchor + offsetDirection.Multiply(leaderOffset);
                    }
                    else if (!HasLeader)
                    {
                        // Leader not enabled - apply minimum offset to avoid placing on host element
                        // Convert from millimeters to feet (Revit internal units)
                        var minimumOffset = MinimumOffsetMillimeters / 304.8; // mm to feet
                        head = anchor + offsetDirection.Multiply(minimumOffset);
                    }
                    else if (freeLength > 0)
                    {
                        // Leader enabled but length is 0 - use free length if available
                        head = anchor + offsetDirection.Multiply(freeLength);
                    }

                    // Adjust position for collision detection if enabled
                    if (collisionDetector != null)
                    {
                        bool foundValidPosition;
                        head = collisionDetector.FindValidPosition(anchor, head, out foundValidPosition);

                        if (!foundValidPosition)
                        {
                            collisionCount++;
                        }
                    }

                    var reference = new Reference(element);

                    // When leader is disabled, Revit places tag at element location regardless of head position
                    // Solution: Create tag WITH leader, position it, then disable leader
                    bool shouldDisableLeaderAfterCreation = !HasLeader;
                    bool createWithLeader = HasLeader || shouldDisableLeaderAfterCreation;

                    var tag = CreateTag(doc, view, reference, head, Orientation, createWithLeader);
                    if (tag == null)
                    {
                        continue;
                    }

                    try
                    {
                        tag.TagHeadPosition = head;

                        // Disable leader after positioning if it wasn't originally enabled
                        if (shouldDisableLeaderAfterCreation)
                        {
                            tag.HasLeader = false;
                        }
                    }
                    catch
                    {
                    }

                    if (resolvedTagTypeId != ElementId.InvalidElementId && tag.GetTypeId() != resolvedTagTypeId)
                    {
                        try
                        {
                            tag.ChangeTypeId(resolvedTagTypeId);
                        }
                        catch
                        {
                        }
                    }

                    double rotationAngle = 0;
                    if (usingDirectionOverride)
                    {
                        // Only rotate tag when element rotation is detected
                        // Otherwise keep tag horizontal regardless of placement direction
                        if (DetectElementRotation && TryGetAngleFromDirection(view, offsetDirection, out var directionAngle))
                        {
                            rotationAngle = directionAngle;
                        }
                    }
                    else
                    {
                        rotationAngle = totalAngle;
                    }

                    // Mark tag as managed by SmartTags
                    if (tag != null)
                    {
                        try
                        {
                            SmartTagMarkerStorage.SetManagedTag(tag, element.Id);
                        }
                        catch
                        {
                            // Continue even if marker storage fails
                        }
                    }

                    // Post-creation validation: Check collision with actual tag bounds
                    if (collisionDetector != null && tag != null)
                    {
                        // Regenerate to ensure bounds are updated
                        doc.Regenerate();

                        // Check if tag's actual bounds collide with obstacles
                        if (collisionDetector.HasCollisionWithActualBounds(tag, out var actualBounds))
                        {
                            // Find new position using actual tag size
                            // Enforce minimum distance from anchor to prevent tags on host elements
                            var minDistance = MinimumOffsetMillimeters / 304.8; // Convert mm to feet
                            bool foundValidPosition;
                            var newHead = collisionDetector.FindValidPositionWithActualSize(anchor, head, actualBounds, out foundValidPosition, minDistance);

                            if (foundValidPosition && (newHead - head).GetLength() > 1e-6)
                            {
                                try
                                {
                                    // When using direction override, constrain movement to element's axis
                                    if (usingDirectionOverride && offsetDirection != null && offsetDirection.GetLength() > 1e-9)
                                    {
                                        // Project newHead onto the line defined by anchor + offsetDirection
                                        // This keeps the leader line aligned with element's axis
                                        var normalizedDirection = offsetDirection.Normalize();
                                        var vectorToNewHead = newHead - anchor;
                                        var projectionLength = vectorToNewHead.DotProduct(normalizedDirection);
                                        newHead = anchor + normalizedDirection.Multiply(projectionLength);
                                    }

                                    // Reposition tag to collision-free location
                                    tag.TagHeadPosition = newHead;

                                    // Update head for tracking
                                    head = newHead;

                                    // Regenerate again after repositioning
                                    doc.Regenerate();
                                }
                                catch
                                {
                                    // If repositioning fails, keep original position
                                    collisionCount++;
                                }
                            }
                            else if (!foundValidPosition)
                            {
                                // No valid position found even with actual size
                                collisionCount++;
                            }
                        }

                        // Add tag with actual bounds to collision detector for subsequent tags
                        collisionDetector.AddNewTag(tag);
                    }

                    // Apply rotation once, after all positioning is complete
                    if (Math.Abs(rotationAngle) > 1e-9)
                    {
                        try
                        {
                            var axis = Line.CreateBound(head, head + viewDirection);
                            ElementTransformUtils.RotateElement(doc, tag.Id, axis, rotationAngle);
                        }
                        catch
                        {
                        }
                    }

                    taggedCount++;
                }

                t.Commit();

                if (taggedCount == 0)
                {
                    TaskDialog.Show("SmartTags", "No taggable elements were found in the active view.");
                }
                else
                {
                    var message = $"Tagged {taggedCount} element(s).";
                    if (collisionCount > 0)
                    {
                        message += $"\n\nWarning: {collisionCount} tag(s) could not avoid collisions.";
                    }
                    TaskDialog.Show("SmartTags", message);
                }
            }
        }

        public string GetName()
        {
            return "SmartTags Tag Placement";
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

        private static bool TryGetAngleFromDirection(View view, XYZ direction, out double angle)
        {
            return TryGetSignedAngleInViewPlane(view, direction, out angle);
        }
    }

    public enum PlacementDirection
    {
        Right,
        Left,
        Up,
        Down
    }
}
