using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SmartTags.Services
{
    public class TagCollisionDetector
    {
        private readonly View _view;
        private readonly double _gapInFeet;
        private List<ObstacleBounds> _obstacles = new List<ObstacleBounds>();
        private List<ObstacleBounds> _newlyCreatedTags = new List<ObstacleBounds>();

        // Estimated tag size for collision checking (conservative estimate)
        // Made larger to account for tags with values/text
        private const double EstimatedTagWidthFeet = 2.0;   // ~600mm - conservative for text
        private const double EstimatedTagHeightFeet = 0.66; // ~200mm - conservative for text height

        public TagCollisionDetector(View view, double gapInMillimeters)
        {
            _view = view ?? throw new ArgumentNullException(nameof(view));
            _gapInFeet = MillimetersToFeet(gapInMillimeters);
        }

        public void CollectObstacles(Document doc, ElementId excludeElementId = null)
        {
            _obstacles.Clear();

            if (doc == null || _view == null)
            {
                return;
            }

            try
            {
                // Collect visible model elements in view
                var visibleElements = new FilteredElementCollector(doc, _view.Id)
                    .WhereElementIsNotElementType()
                    .ToList();

                foreach (var element in visibleElements)
                {
                    try
                    {
                        // Skip the element being tagged
                        if (excludeElementId != null && element.Id == excludeElementId)
                        {
                            continue;
                        }

                        // Skip tags (we'll collect them separately)
                        if (element is IndependentTag)
                        {
                            continue;
                        }

                        var bounds = GetViewPlaneBounds(element);
                        if (bounds != null)
                        {
                            _obstacles.Add(bounds);
                        }
                    }
                    catch
                    {
                        // Skip problematic elements
                        continue;
                    }
                }

                // Explicitly collect text notes (annotations)
                var textNotes = new FilteredElementCollector(doc, _view.Id)
                    .OfClass(typeof(TextNote))
                    .ToList();

                foreach (var textNote in textNotes)
                {
                    try
                    {
                        var bounds = GetViewPlaneBounds(textNote);
                        if (bounds != null)
                        {
                            _obstacles.Add(bounds);
                        }
                    }
                    catch
                    {
                        continue;
                    }
                }

                // Collect existing tags
                var tags = new FilteredElementCollector(doc, _view.Id)
                    .OfClass(typeof(IndependentTag))
                    .Cast<IndependentTag>()
                    .ToList();

                foreach (var tag in tags)
                {
                    try
                    {
                        var bounds = GetTagBounds(tag);
                        if (bounds != null)
                        {
                            _obstacles.Add(bounds);
                        }
                    }
                    catch
                    {
                        // Skip problematic tags
                        continue;
                    }
                }
            }
            catch
            {
                // If obstacle collection fails entirely, continue with empty obstacles list
                _obstacles.Clear();
            }
        }

        public XYZ FindValidPosition(XYZ anchor, XYZ intendedHead, out bool foundValidPosition)
        {
            foundValidPosition = false;

            if (anchor == null || intendedHead == null || _view == null)
            {
                return intendedHead;
            }

            // Fast path: check if intended position is already valid
            if (!HasCollision(intendedHead))
            {
                foundValidPosition = true;
                return intendedHead;
            }

            // Check view direction is valid
            var viewDirection = _view.ViewDirection;
            if (viewDirection == null || viewDirection.GetLength() < 1e-9)
            {
                return intendedHead;
            }

            var right = _view.RightDirection;
            var up = _view.UpDirection;

            if (right == null || up == null || right.GetLength() < 1e-9 || up.GetLength() < 1e-9)
            {
                return intendedHead;
            }

            // Calculate search parameters
            var offset = (intendedHead - anchor).GetLength();
            var initialRadius = offset > 1e-9 ? offset : 0.5; // 6 inches minimum
            var maxRadius = Math.Max(5.0, initialRadius * 3.0); // feet
            var radiusStep = Math.Max(0.1, _view.Scale / 120.0); // scale-dependent step
            var angularSamples = 16;

            XYZ bestCandidate = intendedHead;
            double bestDistance = double.MaxValue;

            // Radial search
            for (double radius = initialRadius; radius <= maxRadius; radius += radiusStep)
            {
                for (int i = 0; i < angularSamples; i++)
                {
                    double angle = (2.0 * Math.PI * i) / angularSamples;

                    // Compute candidate position in view plane
                    var offsetInViewPlane = right.Multiply(radius * Math.Cos(angle))
                        .Add(up.Multiply(radius * Math.Sin(angle)));
                    var candidate = anchor + offsetInViewPlane;

                    // Check collision
                    if (!HasCollision(candidate))
                    {
                        // Calculate distance from intended position
                        var distance = (candidate - intendedHead).GetLength();

                        if (distance < bestDistance)
                        {
                            bestDistance = distance;
                            bestCandidate = candidate;
                            foundValidPosition = true;
                        }
                    }
                }

                // Early exit if we found a good position
                if (foundValidPosition && bestDistance < radiusStep * 2)
                {
                    break;
                }
            }

            return bestCandidate;
        }

        public void AddNewTag(IndependentTag tag)
        {
            if (tag == null)
            {
                return;
            }

            try
            {
                var bounds = GetTagBounds(tag);
                if (bounds != null)
                {
                    _newlyCreatedTags.Add(bounds);
                }
            }
            catch
            {
                // Skip if we can't get bounds
            }
        }

        public bool HasCollisionWithActualBounds(IndependentTag tag, out ObstacleBounds actualBounds)
        {
            actualBounds = null;

            if (tag == null)
            {
                return false;
            }

            try
            {
                actualBounds = GetTagBounds(tag);
                if (actualBounds == null)
                {
                    return false;
                }

                // Check against original obstacles
                foreach (var obstacle in _obstacles)
                {
                    if (actualBounds.Overlaps(obstacle, _gapInFeet))
                    {
                        return true;
                    }
                }

                // Check against newly created tags (excluding this one)
                foreach (var obstacle in _newlyCreatedTags)
                {
                    if (actualBounds.Overlaps(obstacle, _gapInFeet))
                    {
                        return true;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        public XYZ FindValidPositionWithActualSize(XYZ anchor, XYZ intendedHead, ObstacleBounds actualTagBounds, out bool foundValidPosition, double minDistanceFromAnchor = 0.0)
        {
            foundValidPosition = false;

            if (anchor == null || intendedHead == null || actualTagBounds == null || _view == null)
            {
                return intendedHead;
            }

            // Check view direction is valid
            var viewDirection = _view.ViewDirection;
            if (viewDirection == null || viewDirection.GetLength() < 1e-9)
            {
                return intendedHead;
            }

            var right = _view.RightDirection;
            var up = _view.UpDirection;

            if (right == null || up == null || right.GetLength() < 1e-9 || up.GetLength() < 1e-9)
            {
                return intendedHead;
            }

            // Calculate search parameters
            var offset = (intendedHead - anchor).GetLength();
            var initialRadius = Math.Max(offset, Math.Max(minDistanceFromAnchor, 0.5));
            var maxRadius = Math.Max(5.0, initialRadius * 3.0);
            var radiusStep = Math.Max(0.1, _view.Scale / 120.0);
            var angularSamples = 16;

            XYZ bestCandidate = intendedHead;
            double bestDistance = double.MaxValue;

            // Get actual tag dimensions
            double tagWidth = actualTagBounds.MaxX - actualTagBounds.MinX;
            double tagHeight = actualTagBounds.MaxY - actualTagBounds.MinY;
            double halfWidth = tagWidth / 2.0;
            double halfHeight = tagHeight / 2.0;

            // Radial search with actual tag size
            for (double radius = initialRadius; radius <= maxRadius; radius += radiusStep)
            {
                for (int i = 0; i < angularSamples; i++)
                {
                    double angle = (2.0 * Math.PI * i) / angularSamples;

                    var offsetInViewPlane = right.Multiply(radius * Math.Cos(angle))
                        .Add(up.Multiply(radius * Math.Sin(angle)));
                    var candidate = anchor + offsetInViewPlane;

                    // Create bounds at candidate position with actual tag size
                    double centerX = candidate.DotProduct(right);
                    double centerY = candidate.DotProduct(up);

                    var candidateBounds = new ObstacleBounds(
                        centerX - halfWidth,
                        centerX + halfWidth,
                        centerY - halfHeight,
                        centerY + halfHeight
                    );

                    // Check collision with actual bounds
                    bool hasCollision = false;
                    foreach (var obstacle in _obstacles)
                    {
                        if (candidateBounds.Overlaps(obstacle, _gapInFeet))
                        {
                            hasCollision = true;
                            break;
                        }
                    }

                    if (!hasCollision)
                    {
                        foreach (var obstacle in _newlyCreatedTags)
                        {
                            if (candidateBounds.Overlaps(obstacle, _gapInFeet))
                            {
                                hasCollision = true;
                                break;
                            }
                        }
                    }

                    if (!hasCollision)
                    {
                        var distance = (candidate - intendedHead).GetLength();
                        if (distance < bestDistance)
                        {
                            bestDistance = distance;
                            bestCandidate = candidate;
                            foundValidPosition = true;
                        }
                    }
                }

                // Early exit if we found a good position
                if (foundValidPosition && bestDistance < radiusStep * 2)
                {
                    break;
                }
            }

            return bestCandidate;
        }

        private bool HasCollision(XYZ position)
        {
            if (position == null)
            {
                return false;
            }

            // Create estimated tag bounds around the head position
            var tagBounds = CreateEstimatedTagBounds(position);
            if (tagBounds == null)
            {
                return false;
            }

            // Check if tag bounds would overlap with obstacles
            foreach (var obstacle in _obstacles)
            {
                if (tagBounds.Overlaps(obstacle, _gapInFeet))
                {
                    return true;
                }
            }

            // Check against newly created tags
            foreach (var obstacle in _newlyCreatedTags)
            {
                if (tagBounds.Overlaps(obstacle, _gapInFeet))
                {
                    return true;
                }
            }

            return false;
        }

        private ObstacleBounds CreateEstimatedTagBounds(XYZ headPosition)
        {
            if (headPosition == null || _view == null)
            {
                return null;
            }

            var right = _view.RightDirection;
            var up = _view.UpDirection;

            if (right == null || up == null || right.GetLength() < 1e-9 || up.GetLength() < 1e-9)
            {
                return null;
            }

            // Project head position to 2D
            double centerX = headPosition.DotProduct(right);
            double centerY = headPosition.DotProduct(up);

            // Create bounds with estimated tag size
            double halfWidth = EstimatedTagWidthFeet / 2.0;
            double halfHeight = EstimatedTagHeightFeet / 2.0;

            return new ObstacleBounds(
                centerX - halfWidth,
                centerX + halfWidth,
                centerY - halfHeight,
                centerY + halfHeight
            );
        }

        private ObstacleBounds GetViewPlaneBounds(Element element)
        {
            if (element == null || _view == null)
            {
                return null;
            }

            var bbox = element.get_BoundingBox(_view);
            if (bbox == null)
            {
                return null;
            }

            var right = _view.RightDirection;
            var up = _view.UpDirection;

            if (right == null || up == null || right.GetLength() < 1e-9 || up.GetLength() < 1e-9)
            {
                return null;
            }

            // Get 8 corners of 3D bounding box
            var corners = new List<XYZ>
            {
                bbox.Min,
                bbox.Max,
                new XYZ(bbox.Min.X, bbox.Min.Y, bbox.Max.Z),
                new XYZ(bbox.Min.X, bbox.Max.Y, bbox.Min.Z),
                new XYZ(bbox.Max.X, bbox.Min.Y, bbox.Min.Z),
                new XYZ(bbox.Min.X, bbox.Max.Y, bbox.Max.Z),
                new XYZ(bbox.Max.X, bbox.Min.Y, bbox.Max.Z),
                new XYZ(bbox.Max.X, bbox.Max.Y, bbox.Min.Z)
            };

            // Project all corners to 2D and find min/max
            double minX = double.MaxValue;
            double maxX = double.MinValue;
            double minY = double.MaxValue;
            double maxY = double.MinValue;

            foreach (var corner in corners)
            {
                double x = corner.DotProduct(right);
                double y = corner.DotProduct(up);

                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
            }

            return new ObstacleBounds(minX, maxX, minY, maxY);
        }

        private ObstacleBounds GetTagBounds(IndependentTag tag)
        {
            if (tag == null || _view == null)
            {
                return null;
            }

            try
            {
                var bbox = tag.get_BoundingBox(_view);
                if (bbox != null)
                {
                    return GetViewPlaneBounds(tag);
                }

                // Fallback: use tag head position with small buffer
                var head = tag.TagHeadPosition;
                if (head == null)
                {
                    return null;
                }

                var right = _view.RightDirection;
                var up = _view.UpDirection;

                if (right == null || up == null || right.GetLength() < 1e-9 || up.GetLength() < 1e-9)
                {
                    return null;
                }

                double x = head.DotProduct(right);
                double y = head.DotProduct(up);

                // Small buffer (0.1 feet = ~30mm)
                double buffer = 0.1;
                return new ObstacleBounds(x - buffer, x + buffer, y - buffer, y + buffer);
            }
            catch
            {
                return null;
            }
        }

        private static double MillimetersToFeet(double mm)
        {
            // 1 foot = 304.8 millimeters (exact)
            return mm / 304.8;
        }

        public class ObstacleBounds
        {
            public double MinX { get; }
            public double MaxX { get; }
            public double MinY { get; }
            public double MaxY { get; }

            public ObstacleBounds(double minX, double maxX, double minY, double maxY)
            {
                MinX = minX;
                MaxX = maxX;
                MinY = minY;
                MaxY = maxY;
            }

            public bool Overlaps(XYZ point, View view, double gapInFeet)
            {
                if (point == null || view == null)
                {
                    return false;
                }

                var right = view.RightDirection;
                var up = view.UpDirection;

                if (right == null || up == null || right.GetLength() < 1e-9 || up.GetLength() < 1e-9)
                {
                    return false;
                }

                double x = point.DotProduct(right);
                double y = point.DotProduct(up);

                // Check if point is within bounds + gap
                return x >= MinX - gapInFeet && x <= MaxX + gapInFeet &&
                       y >= MinY - gapInFeet && y <= MaxY + gapInFeet;
            }

            public bool Overlaps(ObstacleBounds other, double gapInFeet)
            {
                if (other == null)
                {
                    return false;
                }

                // Check if two rectangles overlap (with gap buffer)
                return !(other.MaxX + gapInFeet < MinX - gapInFeet ||
                         other.MinX - gapInFeet > MaxX + gapInFeet ||
                         other.MaxY + gapInFeet < MinY - gapInFeet ||
                         other.MinY - gapInFeet > MaxY + gapInFeet);
            }
        }
    }
}
