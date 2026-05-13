using Autodesk.Revit.DB;
using System;

namespace SmartTags.Services
{
    public static class DetailLineCenterService
    {
        // DetailCurve overloads - delegate to Curve-based methods

        public static XYZ GetMidpoint(DetailCurve detailCurve)
        {
            if (detailCurve == null)
            {
                return null;
            }

            return GetMidpoint(detailCurve.GeometryCurve);
        }

        public static XYZ GetTangentAtMidpoint(DetailCurve detailCurve, View view)
        {
            if (detailCurve == null || view == null)
            {
                return null;
            }

            return GetTangentAtMidpoint(detailCurve.GeometryCurve, view);
        }

        public static double GetRotationAngle(DetailCurve detailCurve, View view)
        {
            if (detailCurve == null)
            {
                return 0;
            }

            return GetRotationAngle(detailCurve.GeometryCurve, view);
        }

        // Curve-based overloads - used directly for wires and other geometry sources

        public static XYZ GetMidpoint(Curve curve)
        {
            if (curve == null)
            {
                return null;
            }

            try
            {
                return curve.Evaluate(0.5, true);
            }
            catch
            {
                var start = curve.GetEndPoint(0);
                var end = curve.GetEndPoint(1);
                return new XYZ(
                    (start.X + end.X) / 2.0,
                    (start.Y + end.Y) / 2.0,
                    (start.Z + end.Z) / 2.0);
            }
        }

        public static XYZ GetTangentAtMidpoint(Curve curve, View view)
        {
            if (curve == null || view == null)
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
                var start = curve.GetEndPoint(0);
                var end = curve.GetEndPoint(1);
                tangent = end - start;
            }

            if (tangent == null || tangent.GetLength() < 1e-9)
            {
                return view.RightDirection;
            }

            var normal = view.ViewDirection;
            var projected = tangent - normal.Multiply(tangent.DotProduct(normal));
            if (projected.GetLength() < 1e-9)
            {
                return view.RightDirection;
            }

            return projected.Normalize();
        }

        public static double GetRotationAngle(Curve curve, View view)
        {
            var tangent = GetTangentAtMidpoint(curve, view);
            if (tangent == null)
            {
                return 0;
            }

            var right = view.RightDirection;
            var normal = view.ViewDirection;

            var unsignedAngle = right.AngleTo(tangent);
            var cross = right.CrossProduct(tangent);
            var sign = cross.DotProduct(normal) < 0 ? -1.0 : 1.0;

            return unsignedAngle * sign;
        }
    }
}
