using Autodesk.Revit.DB;
using System;

namespace SmartTags.Services
{
    public enum AnchorPoint
    {
        Center,
        TopLeft,
        TopCenter,
        TopRight,
        LeftCenter,
        RightCenter,
        BottomLeft,
        BottomCenter,
        BottomRight
    }

    public static class AnchorPointService
    {
        /// <summary>
        /// Calculate anchor point on element based on selected anchor type.
        /// Works in view-plane coordinates to handle rotated views correctly.
        /// </summary>
        public static bool TryGetAnchorPoint(Element element, View view, AnchorPoint anchorType, out XYZ anchor)
        {
            anchor = null;
            if (element == null || view == null)
            {
                return false;
            }

            // Get element bounding box in view
            var bbox = element.get_BoundingBox(view);
            if (bbox != null)
            {
                anchor = CalculateAnchorFromBoundingBox(bbox, view, anchorType);
                return true;
            }

            // Fallback to location-based anchor for elements without bbox
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
                    // For curves, calculate anchor based on curve endpoints
                    var start = c.GetEndPoint(0);
                    var end = c.GetEndPoint(1);
                    anchor = CalculateAnchorFromPoints(start, end, view, anchorType);
                    return true;
                }
            }

            return false;
        }

        private static XYZ CalculateAnchorFromBoundingBox(BoundingBoxXYZ bbox, View view, AnchorPoint anchorType)
        {
            if (bbox == null || view == null)
            {
                return (bbox.Min + bbox.Max) * 0.5;
            }

            var min = bbox.Min;
            var max = bbox.Max;

            // Get view plane vectors
            var right = view.RightDirection;
            var up = view.UpDirection;

            if (right == null || up == null || right.GetLength() < 1e-9 || up.GetLength() < 1e-9)
            {
                // Fallback to center if view vectors invalid
                return (min + max) * 0.5;
            }

            // Calculate 8 corners of bounding box
            var corners = new XYZ[]
            {
                min,
                max,
                new XYZ(min.X, min.Y, max.Z),
                new XYZ(min.X, max.Y, min.Z),
                new XYZ(max.X, min.Y, min.Z),
                new XYZ(min.X, max.Y, max.Z),
                new XYZ(max.X, min.Y, max.Z),
                new XYZ(max.X, max.Y, min.Z)
            };

            // Project corners to view plane and find extents
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

            // Calculate anchor point in 2D view plane
            double anchorX, anchorY;

            switch (anchorType)
            {
                case AnchorPoint.TopLeft:
                    anchorX = minX;
                    anchorY = maxY;
                    break;
                case AnchorPoint.TopCenter:
                    anchorX = (minX + maxX) * 0.5;
                    anchorY = maxY;
                    break;
                case AnchorPoint.TopRight:
                    anchorX = maxX;
                    anchorY = maxY;
                    break;
                case AnchorPoint.LeftCenter:
                    anchorX = minX;
                    anchorY = (minY + maxY) * 0.5;
                    break;
                case AnchorPoint.RightCenter:
                    anchorX = maxX;
                    anchorY = (minY + maxY) * 0.5;
                    break;
                case AnchorPoint.BottomLeft:
                    anchorX = minX;
                    anchorY = minY;
                    break;
                case AnchorPoint.BottomCenter:
                    anchorX = (minX + maxX) * 0.5;
                    anchorY = minY;
                    break;
                case AnchorPoint.BottomRight:
                    anchorX = maxX;
                    anchorY = minY;
                    break;
                case AnchorPoint.Center:
                default:
                    anchorX = (minX + maxX) * 0.5;
                    anchorY = (minY + maxY) * 0.5;
                    break;
            }

            // Convert 2D view-plane coordinates back to 3D world coordinates
            // Start with bbox center as reference point, then apply 2D offsets
            var center3D = (min + max) * 0.5;

            // Calculate center in 2D view coordinates
            var centerX = (minX + maxX) * 0.5;
            var centerY = (minY + maxY) * 0.5;

            // Calculate offsets from center
            var offsetX = anchorX - centerX;
            var offsetY = anchorY - centerY;

            // Apply offsets to 3D center point
            return center3D.Add(right.Multiply(offsetX)).Add(up.Multiply(offsetY));
        }

        private static XYZ CalculateAnchorFromPoints(XYZ start, XYZ end, View view, AnchorPoint anchorType)
        {
            // For curves/lines, treat as a 1D element
            // Map anchor types to positions along the curve
            switch (anchorType)
            {
                case AnchorPoint.TopLeft:
                case AnchorPoint.LeftCenter:
                case AnchorPoint.BottomLeft:
                    return start;
                case AnchorPoint.TopRight:
                case AnchorPoint.RightCenter:
                case AnchorPoint.BottomRight:
                    return end;
                case AnchorPoint.TopCenter:
                case AnchorPoint.Center:
                case AnchorPoint.BottomCenter:
                default:
                    return (start + end) * 0.5;
            }
        }
    }
}
