using System;
using System.Collections.Generic;

namespace SmartTags.Services
{
    /// <summary>
    /// Uniform grid-based 2D spatial index for broad-phase collision filtering.
    /// Operates in view-plane coordinates for correct behavior in rotated views.
    /// </summary>
    public class SpatialIndex2D
    {
        private readonly double _cellSize;
        private readonly Dictionary<(int, int), List<TagCollisionDetector.ObstacleBounds>> _grid;
        private double _minX = double.MaxValue;
        private double _maxX = double.MinValue;
        private double _minY = double.MaxValue;
        private double _maxY = double.MinValue;

        public SpatialIndex2D(double cellSize = 5.0)
        {
            _cellSize = cellSize > 0 ? cellSize : 5.0;
            _grid = new Dictionary<(int, int), List<TagCollisionDetector.ObstacleBounds>>();
        }

        public void AddObstacle(TagCollisionDetector.ObstacleBounds bounds)
        {
            if (bounds == null)
            {
                return;
            }

            // Update overall bounds
            _minX = Math.Min(_minX, bounds.MinX);
            _maxX = Math.Max(_maxX, bounds.MaxX);
            _minY = Math.Min(_minY, bounds.MinY);
            _maxY = Math.Max(_maxY, bounds.MaxY);

            // Determine which grid cells this obstacle overlaps
            int minCellX = GetCellIndex(bounds.MinX);
            int maxCellX = GetCellIndex(bounds.MaxX);
            int minCellY = GetCellIndex(bounds.MinY);
            int maxCellY = GetCellIndex(bounds.MaxY);

            // Add to all overlapping cells
            for (int x = minCellX; x <= maxCellX; x++)
            {
                for (int y = minCellY; y <= maxCellY; y++)
                {
                    var key = (x, y);
                    if (!_grid.ContainsKey(key))
                    {
                        _grid[key] = new List<TagCollisionDetector.ObstacleBounds>();
                    }
                    _grid[key].Add(bounds);
                }
            }
        }

        public List<TagCollisionDetector.ObstacleBounds> GetNearbyObstacles(TagCollisionDetector.ObstacleBounds queryBounds)
        {
            if (queryBounds == null)
            {
                return new List<TagCollisionDetector.ObstacleBounds>();
            }

            var result = new HashSet<TagCollisionDetector.ObstacleBounds>();

            int minCellX = GetCellIndex(queryBounds.MinX);
            int maxCellX = GetCellIndex(queryBounds.MaxX);
            int minCellY = GetCellIndex(queryBounds.MinY);
            int maxCellY = GetCellIndex(queryBounds.MaxY);

            for (int x = minCellX; x <= maxCellX; x++)
            {
                for (int y = minCellY; y <= maxCellY; y++)
                {
                    var key = (x, y);
                    if (_grid.TryGetValue(key, out var obstacles))
                    {
                        foreach (var obstacle in obstacles)
                        {
                            result.Add(obstacle);
                        }
                    }
                }
            }

            return new List<TagCollisionDetector.ObstacleBounds>(result);
        }

        public List<TagCollisionDetector.ObstacleBounds> GetNearbyObstacles(double centerX, double centerY, double radius)
        {
            if (radius <= 0)
            {
                return new List<TagCollisionDetector.ObstacleBounds>();
            }

            var queryBounds = new TagCollisionDetector.ObstacleBounds(
                centerX - radius,
                centerX + radius,
                centerY - radius,
                centerY + radius
            );

            return GetNearbyObstacles(queryBounds);
        }

        public void Clear()
        {
            _grid.Clear();
            _minX = double.MaxValue;
            _maxX = double.MinValue;
            _minY = double.MaxValue;
            _maxY = double.MinValue;
        }

        public int GetObstacleCount()
        {
            var uniqueObstacles = new HashSet<TagCollisionDetector.ObstacleBounds>();
            foreach (var cell in _grid.Values)
            {
                foreach (var obstacle in cell)
                {
                    uniqueObstacles.Add(obstacle);
                }
            }
            return uniqueObstacles.Count;
        }

        private int GetCellIndex(double coordinate)
        {
            return (int)Math.Floor(coordinate / _cellSize);
        }
    }
}
