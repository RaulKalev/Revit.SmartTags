using Autodesk.Revit.DB;
using SmartTags.ExternalEvents;
using System;

namespace SmartTags.Services
{
    /// <summary>
    /// Separates tag type selection from tag orientation/rotation logic.
    /// Left/Right/Up/Down are interpreted in view coordinates, not world axes.
    /// </summary>
    public class TagTypeOrientationResolver
    {
        /// <summary>
        /// Resolve which tag type to use based on direction override settings.
        /// This is independent of tag rotation.
        /// </summary>
        public static ElementId ResolveTagType(
            ElementId baseTagTypeId,
            DirectionTagTypeResolver directionResolver,
            PlacementDirection direction)
        {
            if (directionResolver == null)
            {
                return baseTagTypeId;
            }

            ElementId directionSpecificTypeId = directionResolver.ResolveTagTypeForDirection(direction);

            // Use direction-specific type if available and different from base
            if (directionSpecificTypeId != null &&
                directionSpecificTypeId != ElementId.InvalidElementId &&
                directionSpecificTypeId != baseTagTypeId)
            {
                return directionSpecificTypeId;
            }

            return baseTagTypeId;
        }

        /// <summary>
        /// Resolve tag orientation (rotation angle) based on settings.
        /// Separated from tag type selection for clarity.
        /// </summary>
        public static double ResolveOrientation(
            double baseAngle,
            bool detectElementRotation,
            bool usingDirectionOverride,
            Element element,
            View view,
            XYZ offsetDirection)
        {
            if (element == null || view == null)
            {
                return baseAngle;
            }

            double elementAngle = 0;
            bool hasElementRotation = false;

            if (detectElementRotation)
            {
                hasElementRotation = TryGetElementRotationAngle(element, view, out elementAngle);
            }

            // Decision logic:
            // 1. If using direction override:
            //    - Only rotate when element rotation is detected
            //    - Otherwise keep tag horizontal (direction type handles visual direction)
            // 2. If NOT using direction override:
            //    - Apply base angle + element rotation (if detected)

            if (usingDirectionOverride)
            {
                // Direction override controls tag type selection
                // Tag rotation only applies when element has rotation
                if (hasElementRotation && offsetDirection != null)
                {
                    // Rotate tag to align with element orientation
                    if (TryGetAngleFromDirection(view, offsetDirection, out var directionAngle))
                    {
                        return directionAngle;
                    }
                }

                // Keep tag horizontal when using direction types without element rotation
                return 0;
            }
            else
            {
                // No direction override - use base angle + element rotation
                return baseAngle + (hasElementRotation ? elementAngle : 0);
            }
        }

        /// <summary>
        /// Calculate offset direction vector after applying rotations.
        /// This determines where the tag will be placed relative to the element.
        /// </summary>
        public static XYZ ResolveOffsetDirection(
            XYZ baseDirectionVector,
            View view,
            bool detectElementRotation,
            bool usingDirectionOverride,
            Element element,
            double baseAngle)
        {
            if (baseDirectionVector == null || view == null)
            {
                return baseDirectionVector;
            }

            var viewDirection = view.ViewDirection;
            var viewAxis = viewDirection != null && viewDirection.GetLength() > 1e-9
                ? viewDirection.Normalize()
                : XYZ.BasisZ;

            double rotationAngle = 0;
            bool hasElementRotation = false;

            if (detectElementRotation)
            {
                hasElementRotation = TryGetElementRotationAngle(element, view, out var elementAngle);

                if (hasElementRotation)
                {
                    rotationAngle = elementAngle;
                }
            }

            // Apply rotation based on context
            if (usingDirectionOverride)
            {
                // When using direction override, only rotate offset if element has rotation
                if (hasElementRotation)
                {
                    return RotateVectorAroundAxis(baseDirectionVector, viewAxis, rotationAngle);
                }

                return baseDirectionVector;
            }
            else
            {
                // No direction override - apply base angle + element rotation
                double totalAngle = baseAngle + (hasElementRotation ? rotationAngle : 0);
                return RotateVectorAroundAxis(baseDirectionVector, viewAxis, totalAngle);
            }
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

        private static bool TryGetAngleFromDirection(View view, XYZ direction, out double angle)
        {
            return TryGetSignedAngleInViewPlane(view, direction, out angle);
        }
    }
}
