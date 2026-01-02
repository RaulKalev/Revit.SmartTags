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
        public bool HasLeader { get; set; }
        public double AttachedLength { get; set; }
        public double FreeLength { get; set; }
        public TagOrientation Orientation { get; set; } = TagOrientation.Horizontal;
        public double Angle { get; set; }
        public bool EnableCollisionDetection { get; set; }
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

            TagCollisionDetector collisionDetector = null;
            if (EnableCollisionDetection)
            {
                collisionDetector = new TagCollisionDetector(view, CollisionGapMillimeters);
                collisionDetector.CollectObstacles(doc);
            }

            var viewDirection = view.ViewDirection;
            var viewAxis = viewDirection != null && viewDirection.GetLength() > 1e-9
                ? viewDirection.Normalize()
                : XYZ.BasisZ;
            var scaleFactor = Math.Max(1, view.Scale);
            var attachedLength = AttachedLength * scaleFactor;
            var freeLength = FreeLength * scaleFactor;

            foreach (var tag in tags)
            {
                try
                {
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

            if (!TryGetAnchorPoint(element, view, out var anchor))
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

            if (collisionDetector != null)
            {
                bool foundValidPosition;
                head = collisionDetector.FindValidPosition(anchor, head, out foundValidPosition);
            }

            var newState = new TagStateSnapshot
            {
                TagHeadPosition = head,
                HasLeader = HasLeader,
                Orientation = Orientation,
                LeaderEndCondition = oldState.LeaderEndCondition
            };

            var reason = "Applying current SmartTags settings";
            return new TagAdjustmentProposal(tag.Id, referencedElementId, oldState, newState, reason);
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
