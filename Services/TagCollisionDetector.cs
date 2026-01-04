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
        private SpatialIndex2D _spatialIndex;
        private SpatialIndex2D _newTagsIndex;

        // Estimated tag size for collision checking (conservative estimate)
        // Made larger to account for tags with values/text
        private const double EstimatedTagWidthFeet = 2.0;   // ~600mm - conservative for text
        private const double EstimatedTagHeightFeet = 0.66; // ~200mm - conservative for text height

        // Performance diagnostics
        private int _collisionChecksPerformed = 0;
        private int _spatialFilteredChecks = 0;

        public TagCollisionDetector(View view, double gapInMillimeters)
        {
            _view = view ?? throw new ArgumentNullException(nameof(view));
            _gapInFeet = MillimetersToFeet(gapInMillimeters);

            // Cell size ~5 feet for good spatial partitioning
            _spatialIndex = new SpatialIndex2D(5.0);
            _newTagsIndex = new SpatialIndex2D(5.0);
        }

        public void CollectObstacles(Document doc, ElementId excludeElementId = null)
        {
            CollectObstaclesExcludingTags(doc, null, excludeElementId);
        }

        public void CollectObstaclesExcludingTags(Document doc, HashSet<ElementId> excludeTagIds, ElementId excludeElementId = null)
        {
            _obstacles.Clear();
            _spatialIndex.Clear();
            _collisionChecksPerformed = 0;
            _spatialFilteredChecks = 0;

            if (doc == null || _view == null)
            {
                return;
            }

            try
            {
                // Collect ONLY tags and specific model categories that would visually block tags in 2D
                // This whitelist approach prevents 3D elements above/below view plane from causing false collisions
                
                // First, collect all tags (these are always relevant)
                var tags = new FilteredElementCollector(doc, _view.Id)
                    .OfClass(typeof(IndependentTag))
                    .Cast<IndependentTag>()
                    .Where(t => !t.IsHidden(_view))
                    .Cast<Element>()
                    .ToList();

                // Then collect specific model element categories that are actually visible in 2D
                var modelElements = new FilteredElementCollector(doc, _view.Id)
                    .WhereElementIsNotElementType()
                    .Where(e =>
                    {
                        try
                        {
                            if (e is IndependentTag) return false; // Already collected
                            if (e.IsHidden(_view)) return false;
                            if (e.Category == null || !e.Category.get_Visible(_view)) return false;
                            
                            var catName = e.Category.Name;
                            
                            // WHITELIST: Only include categories that would actually block tags in 2D plan/section views
                            // Add more categories as needed, but be conservative to avoid 3D element pollution
                            if (catName == "Walls" ||
                                catName == "Doors" ||
                                catName == "Windows" ||
                                catName == "Furniture" ||
                                catName == "Ducts" ||
                                catName == "Pipes" ||
                                catName == "Conduits" ||
                                catName == "Cable Trays" ||
                                catName == "Structural Framing" ||
                                catName == "Structural Columns" ||
                                catName == "Columns" ||
                                catName == "Floors" ||
                                catName == "Roofs" ||
                                catName == "Ceilings" ||
                                catName == "Detail Items" ||
                                catName == "Generic Models" ||
                                catName == "Casework" ||
                                catName == "Plumbing Fixtures" ||
                                catName == "Lighting Fixtures" ||
                                catName == "Electrical Equipment" ||
                                catName == "Mechanical Equipment" ||
                                catName == "Specialty Equipment")
                            {
                                var bbox = e.get_BoundingBox(_view);
                                return bbox != null;
                            }
                            
                            return false;
                        }
                        catch
                        {
                            return false;
                        }
                    })
                    .ToList();

                var allElements = tags.Concat(modelElements).ToList();

                foreach (var element in allElements)
                {
                    try
                    {
                        // Skip the element being tagged
                        if (excludeElementId != null && element.Id == excludeElementId)
                        {
                            continue;
                        }

                        // Skip tags being normalized (to prevent self-collision)
                        if (excludeTagIds != null && element is IndependentTag && excludeTagIds.Contains(element.Id))
                        {
                            continue;
                        }

                        ObstacleBounds bounds = null;

                        // Get bounds based on element type
                        if (element is IndependentTag tag)
                        {
                            bounds = GetTagBounds(tag);
                        }
                        else
                        {
                            bounds = GetViewPlaneBounds(element);
                        }

                        if (bounds != null)
                        {
                            _obstacles.Add(bounds);
                            _spatialIndex.AddObstacle(bounds);
                        }
                    }
                    catch
                    {
                        // Skip problematic elements
                        continue;
                    }
                }
            }
            catch
            {
                // If obstacle collection fails entirely, continue with empty obstacles list
                _obstacles.Clear();
                _spatialIndex.Clear();
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
                    _newTagsIndex.AddObstacle(bounds);
                    System.Diagnostics.Debug.WriteLine($"[Collision] Added new tag to tracker. Total new tags: {_newlyCreatedTags.Count}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[Collision] WARNING: Could not get bounds for new tag!");
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

                    // Use spatial index for broad-phase filtering
                    var nearbyObstacles = _spatialIndex.GetNearbyObstacles(candidateBounds);
                    _spatialFilteredChecks += nearbyObstacles.Count;

                    bool hasCollision = false;
                    foreach (var obstacle in nearbyObstacles)
                    {
                        _collisionChecksPerformed++;
                        if (candidateBounds.Overlaps(obstacle, _gapInFeet))
                        {
                            hasCollision = true;
                            break;
                        }
                    }

                    if (!hasCollision)
                    {
                        var nearbyNewTags = _newTagsIndex.GetNearbyObstacles(candidateBounds);
                        foreach (var obstacle in nearbyNewTags)
                        {
                            _collisionChecksPerformed++;
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

            // Fallback strategy when no valid position found
            if (!foundValidPosition)
            {
                System.Diagnostics.Debug.WriteLine("[SmartTags] No collision-free position found, using fallback strategy");

                // Try least-overlap candidate as last resort
                bestCandidate = SelectLeastOverlapCandidate(anchor, actualTagBounds, initialRadius, maxRadius);
            }

            return bestCandidate;
        }

        public bool HasCollisionAtPosition(XYZ position)
        {
            return HasCollision(position);
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

            // Use spatial index for broad-phase filtering
            var nearbyObstacles = _spatialIndex.GetNearbyObstacles(tagBounds);
            _spatialFilteredChecks += nearbyObstacles.Count;

            foreach (var obstacle in nearbyObstacles)
            {
                _collisionChecksPerformed++;
                if (tagBounds.Overlaps(obstacle, _gapInFeet))
                {
                    System.Diagnostics.Debug.WriteLine($"[Collision] Found overlap with obstacle at estimated position");
                    return true;
                }
            }

            // Check against newly created tags (usually small list, spatial index optional)
            var nearbyNewTags = _newTagsIndex.GetNearbyObstacles(tagBounds);
            System.Diagnostics.Debug.WriteLine($"[Collision] Checking {nearbyNewTags.Count} nearby new tags");
            foreach (var obstacle in nearbyNewTags)
            {
                _collisionChecksPerformed++;
                if (tagBounds.Overlaps(obstacle, _gapInFeet))
                {
                    System.Diagnostics.Debug.WriteLine($"[Collision] Found overlap with NEW TAG at estimated position");
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

        /// <summary>
        /// Compute overlap area between two 2D rectangles
        /// </summary>
        private static double ComputeOverlapArea(ObstacleBounds a, ObstacleBounds b)
        {
            if (a == null || b == null)
            {
                return 0;
            }

            double overlapWidth = Math.Max(0, Math.Min(a.MaxX, b.MaxX) - Math.Max(a.MinX, b.MinX));
            double overlapHeight = Math.Max(0, Math.Min(a.MaxY, b.MaxY) - Math.Max(a.MinY, b.MinY));

            return overlapWidth * overlapHeight;
        }

        /// <summary>
        /// Select candidate with least overlap when no collision-free position exists
        /// </summary>
        private XYZ SelectLeastOverlapCandidate(XYZ anchor, ObstacleBounds tagBounds, double minDistance, double maxRadius)
        {
            if (anchor == null || tagBounds == null || _view == null)
            {
                return anchor;
            }

            var right = _view.RightDirection;
            var up = _view.UpDirection;

            if (right == null || up == null || right.GetLength() < 1e-9 || up.GetLength() < 1e-9)
            {
                return anchor;
            }

            double tagWidth = tagBounds.MaxX - tagBounds.MinX;
            double tagHeight = tagBounds.MaxY - tagBounds.MinY;
            double halfWidth = tagWidth / 2.0;
            double halfHeight = tagHeight / 2.0;

            var radiusStep = Math.Max(0.1, _view.Scale / 120.0);
            var angularSamples = 16;

            XYZ bestCandidate = anchor + right.Multiply(minDistance);
            double leastOverlap = double.MaxValue;

            for (double radius = minDistance; radius <= maxRadius; radius += radiusStep)
            {
                for (int i = 0; i < angularSamples; i++)
                {
                    double angle = (2.0 * Math.PI * i) / angularSamples;

                    var offsetInViewPlane = right.Multiply(radius * Math.Cos(angle))
                        .Add(up.Multiply(radius * Math.Sin(angle)));
                    var candidate = anchor + offsetInViewPlane;

                    double centerX = candidate.DotProduct(right);
                    double centerY = candidate.DotProduct(up);

                    var candidateBounds = new ObstacleBounds(
                        centerX - halfWidth,
                        centerX + halfWidth,
                        centerY - halfHeight,
                        centerY + halfHeight
                    );

                    // Calculate total overlap area with all obstacles
                    double totalOverlap = 0;
                    var nearbyObstacles = _spatialIndex.GetNearbyObstacles(candidateBounds);

                    foreach (var obstacle in nearbyObstacles)
                    {
                        totalOverlap += ComputeOverlapArea(candidateBounds, obstacle);
                    }

                    var nearbyNewTags = _newTagsIndex.GetNearbyObstacles(candidateBounds);
                    foreach (var obstacle in nearbyNewTags)
                    {
                        totalOverlap += ComputeOverlapArea(candidateBounds, obstacle);
                    }

                    if (totalOverlap < leastOverlap)
                    {
                        leastOverlap = totalOverlap;
                        bestCandidate = candidate;

                        // Perfect candidate found
                        if (totalOverlap < 1e-9)
                        {
                            return bestCandidate;
                        }
                    }
                }
            }

            // Log warning if we had to accept overlap
            if (leastOverlap > 1e-9)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[SmartTags] Warning: No collision-free position found. Using least-overlap candidate with {leastOverlap:F3} sq ft overlap.");
            }

            return bestCandidate;
        }

        /// <summary>
        /// Get performance diagnostics
        /// </summary>
        public string GetPerformanceDiagnostics()
        {
            int totalObstacles = _obstacles.Count + _newlyCreatedTags.Count;
            return $"Collision checks: {_collisionChecksPerformed}, Spatial filtered: {_spatialFilteredChecks}, Total obstacles: {totalObstacles}";
        }

        public int GetNewTagCount()
        {
            return _newlyCreatedTags.Count;
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
