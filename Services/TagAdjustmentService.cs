using Autodesk.Revit.DB;
using SmartTags.ExternalEvents;
using SmartTags.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SmartTags.Services
{
    public class TagAdjustmentService
    {
        public PlacementDirection Direction { get; set; } = PlacementDirection.Right;
        public bool DetectElementRotation { get; set; }
        public AnchorPoint AnchorPoint { get; set; } = AnchorPoint.Center;
        public bool HasLeader { get; set; }
        public double AttachedLength { get; set; }
        public double FreeLength { get; set; }
        public TagOrientation Orientation { get; set; } = TagOrientation.Horizontal;
        public double Angle { get; set; }
        public bool EnableCollisionDetection { get; set; }
        public LeaderEndCondition LeaderEndCondition { get; set; } = LeaderEndCondition.Attached;
        public double CollisionGapMillimeters { get; set; } = 1.0;
        public double MinimumOffsetMillimeters { get; set; } = 300.0;

        public List<TagAdjustmentProposal> ComputeAdjustments(
            Document doc,
            View view,
            List<IndependentTag> tags)
        {
            var proposals = new List<TagAdjustmentProposal>();

            if (doc == null || view == null || tags == null || tags.Count == 0)
            {
                return proposals;
            }

            var viewDirection = view.ViewDirection;
            var viewAxis = viewDirection != null && viewDirection.GetLength() > 1e-9
                ? viewDirection.Normalize()
                : XYZ.BasisZ;
            var scaleFactor = Math.Max(1, view.Scale);
            var attachedLength = AttachedLength * scaleFactor;
            var freeLength = FreeLength * scaleFactor;

            // Sort tags by distance from their host elements (farthest first)
            // This ensures that when tags collide with each other, the farthest one moves first
            var sortedTags = tags.OrderByDescending(tag =>
            {
                try
                {
#if NET8_0_OR_GREATER
                    var taggedIds = tag.GetTaggedElementIds();
                    if (taggedIds == null || taggedIds.Count == 0) return 0.0;
                    var elementId = taggedIds.FirstOrDefault()?.HostElementId;
#else
                    var references = tag.GetTaggedReferences();
                    if (references == null || references.Count == 0) return 0.0;
                    var elementId = references[0].ElementId;
#endif
                    if (elementId == null || elementId == ElementId.InvalidElementId) return 0.0;

                    var element = doc.GetElement(elementId);
                    if (element == null) return 0.0;

                    if (!AnchorPointService.TryGetAnchorPoint(element, view, AnchorPoint, out var anchor))
                        return 0.0;

                    var tagHead = tag.TagHeadPosition;
                    if (tagHead == null) return 0.0;

                    return (tagHead - anchor).GetLength();
                }
                catch
                {
                    return 0.0;
                }
            }).ToList();

            // Track tags that have already been processed and will move
            // This prevents closer tags from moving when farther tags have already been flagged to move
            var processedTagsWithMovement = new HashSet<ElementId>();

            foreach (var tag in sortedTags)
            {
                try
                {
                    // Create fresh collision detector for each tag
                    // Exclude this tag AND any tags that have already been processed with movement
                    // This way, if a farther tag is moving, closer tags won't see it as an obstacle
                    TagCollisionDetector collisionDetector = null;
                    if (EnableCollisionDetection)
                    {
                        collisionDetector = new TagCollisionDetector(view, CollisionGapMillimeters);
                        var excludeTags = new HashSet<ElementId>(processedTagsWithMovement) { tag.Id };
                        collisionDetector.CollectObstaclesExcludingTags(doc, excludeTags);
                    }

                    var proposal = ComputeSingleAdjustment(
                        doc,
                        view,
                        tag,
                        collisionDetector,
                        viewAxis,
                        viewDirection,
                        attachedLength,
                        freeLength);

                    if (proposal != null && proposal.IsSignificantChange())
                    {
                        proposals.Add(proposal);
                        // Track that this tag will move, so subsequent tags don't consider it an obstacle
                        processedTagsWithMovement.Add(tag.Id);
                    }
                }
                catch
                {
                    continue;
                }
            }

            return proposals;
        }

        private TagAdjustmentProposal ComputeSingleAdjustment(
            Document doc,
            View view,
            IndependentTag tag,
            TagCollisionDetector collisionDetector,
            XYZ viewAxis,
            XYZ viewDirection,
            double attachedLength,
            double freeLength)
        {
            if (tag == null)
            {
                return null;
            }

            var oldState = new TagStateSnapshot(tag);

#if NET8_0_OR_GREATER
            var taggedIds = tag.GetTaggedElementIds();
            if (taggedIds == null || taggedIds.Count == 0)
            {
                return null;
            }
            var referencedElementId = taggedIds.FirstOrDefault()?.HostElementId;
            if (referencedElementId == null || referencedElementId == ElementId.InvalidElementId)
            {
                return null;
            }
#else
            var references = tag.GetTaggedReferences();
            if (references == null || references.Count == 0)
            {
                return null;
            }
            var referencedElementId = references[0].ElementId;
#endif

            var element = doc.GetElement(referencedElementId);
            if (element == null)
            {
                return null;
            }

            if (!AnchorPointService.TryGetAnchorPoint(element, view, AnchorPoint, out var anchor))
            {
                return null;
            }

            var totalAngle = Angle;
            if (DetectElementRotation && TryGetElementRotationAngle(element, view, out var elementAngle))
            {
                totalAngle += elementAngle;
            }

            var directionVector = GetDirectionVector(view, Direction);
            var offsetDirection = RotateVectorAroundAxis(directionVector, viewAxis, totalAngle);
            var head = anchor;
            var leaderOffset = Math.Max(0, attachedLength + freeLength);

            // Use leader offset for placement regardless of HasLeader state
            // HasLeader only controls whether the visual leader line is shown
            if (leaderOffset > 0)
            {
                head = anchor + offsetDirection.Multiply(leaderOffset);
            }
            else
            {
                // No leader length specified - use minimum offset
                var minimumOffset = MinimumOffsetMillimeters / 304.8;
                head = anchor + offsetDirection.Multiply(minimumOffset);
            }

            // Check if current tag position has a collision
            // Tags are processed farthest-from-host first, so if tags collide with each other,
            // the farther one will be moved first
            bool hasCurrentCollision = false;
            if (collisionDetector != null)
            {
                hasCurrentCollision = collisionDetector.HasCollisionAtPosition(oldState.TagHeadPosition);

                // Only reposition if there's a collision at current position
                if (hasCurrentCollision)
                {
                    bool foundValidPosition;
                    head = collisionDetector.FindValidPosition(anchor, head, out foundValidPosition);
                }
                else
                {
                    // No collision - keep tag where it is
                    head = oldState.TagHeadPosition;
                }
            }

            var newState = new TagStateSnapshot
            {
                TagHeadPosition = head,
                HasLeader = HasLeader,
                Orientation = Orientation,
                LeaderEndCondition = HasLeader ? LeaderEndCondition : oldState.LeaderEndCondition
            };

            var reason = "Applying current SmartTags settings";
            return new TagAdjustmentProposal(tag.Id, referencedElementId, oldState, newState, reason);
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
