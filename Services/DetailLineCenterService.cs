using Autodesk.Revit.DB;
using System;

namespace SmartTags.Services
{
    public static class DetailLineCenterService
    {
        public static XYZ GetMidpoint(DetailCurve detailCurve)
        {
            if (detailCurve == null)
            {
                return null;
            }

            var curve = detailCurve.GeometryCurve;
            if (curve == null)
            {
                return null;
            }

            try
            {
                // Evaluate at normalized parameter 0.5 gives the geometric midpoint by arc length
                return curve.Evaluate(0.5, true);
            }
            catch
            {
                // Fallback: use arithmetic midpoint of endpoints
                var start = curve.GetEndPoint(0);
                var end = curve.GetEndPoint(1);
                return new XYZ(
                    (start.X + end.X) / 2.0,
                    (start.Y + end.Y) / 2.0,
                    (start.Z + end.Z) / 2.0);
            }
        }

        public static XYZ GetTangentAtMidpoint(DetailCurve detailCurve, View view)
        {
            if (detailCurve == null || view == null)
            {
                return null;
            }

            var curve = detailCurve.GeometryCurve;
            if (curve == null)
            {
                return null;
            }

            XYZ tangent;
            try
            {
                var transform = curve.ComputeDerivatives(0.5, true);
                tangent = transform.BasisX;
            }
            catch
            {
                // Fallback: use chord direction
                var start = curve.GetEndPoint(0);
                var end = curve.GetEndPoint(1);
                tangent = end - start;
            }

            if (tangent == null || tangent.GetLength() < 1e-9)
            {
                return view.RightDirection;
            }

            // Project onto view plane to ensure we work in 2D view coordinates
            var normal = view.ViewDirection;
            var projected = tangent - normal.Multiply(tangent.DotProduct(normal));
            if (projected.GetLength() < 1e-9)
            {
                return view.RightDirection;
            }

            return projected.Normalize();
        }

        public static double GetRotationAngle(DetailCurve detailCurve, View view)
        {
            var tangent = GetTangentAtMidpoint(detailCurve, view);
            if (tangent == null)
            {
                return 0;
            }

            // Angle of tangent relative to view's right direction, measured in view plane
            var right = view.RightDirection;
            var up = view.UpDirection;
            var normal = view.ViewDirection;

            // Signed angle from RightDirection to tangent around view normal
            var unsignedAngle = right.AngleTo(tangent);
            var cross = right.CrossProduct(tangent);
            var sign = cross.DotProduct(normal) < 0 ? -1.0 : 1.0;

            return unsignedAngle * sign;
        }
    }
}
