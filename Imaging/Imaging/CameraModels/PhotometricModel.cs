using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;

namespace OPS.Imaging
{
    public class PhotometricModel : CameraModel
    {
        /// <summary>
        /// Horizontal and vertical focal lengths.
        /// </summary>
        public readonly Vector2 FocalLengths;
        /// <summary>
        /// Image coordinate of principal point.
        /// </summary>
        public readonly Vector2 PrincipalPoint;
        /// <summary>
        /// Angle between X axis and horizontal axis of image sensor.
        /// </summary>
        public readonly double HorizontalAngle;

        internal readonly Vector3 HorizontalAxis;

        public PhotometricModel(Vector2 focalLengths, Vector2 principalPoint, double horizontalAngle)
        {
            FocalLengths = focalLengths;
            PrincipalPoint = principalPoint;
            HorizontalAngle = horizontalAngle;

            HorizontalAxis = new Vector3(Math.Cos(HorizontalAngle), Math.Sin(HorizontalAngle), 0);
        }

        public override bool Linear
        {
            get
            {
                return true;
            }
        }

        public override Vector2 Backproject(Vector3 pos, out double range)
        {
            if (Math.Abs(pos.Z) < 1e-15) throw new DivideByZeroException();
            range = pos.Length();
            return new Vector2(
                (pos.Dot(HorizontalAxis) * FocalLengths.X) / pos.Z + PrincipalPoint.X,
                (pos.Y * FocalLengths.Y) / pos.Z + PrincipalPoint.Y
                );
        }

        public override object Clone()
        {
            return new PhotometricModel(FocalLengths, PrincipalPoint, HorizontalAngle);
        }

        public override void ProjectRay(ref Vector2 pixelPos, out Ray ray)
        {
            // [ 0, f_y, p_y - y] x [h_x, h_y, p_x - x]
            // =
            // [
            //   (   f_y   ) * ( p_x - x ) - ( p_y - y ) * (   h_y   ),
            //   ( p_y - y ) * (   h_x   ) - (    0    ) * ( p_x - x ),
            //   (    0    ) * (   h_y   ) - (   f_y   ) * (   h_x   )
            // ]

            Vector3 uvec3 = new Vector3(
                FocalLengths.Y * (PrincipalPoint.X - pixelPos.X) - (PrincipalPoint.Y - pixelPos.Y) * HorizontalAxis.Y,
                (PrincipalPoint.Y - pixelPos.Y) * FocalLengths.X,
                -FocalLengths.Y * HorizontalAxis.X
                );
            ray = new Ray(Vector3.Zero, uvec3);
        }

        /// <summary>
        /// Decompose a CAHV camera model into a photometric camera model and rigid transform.
        /// </summary>
        /// <param name="cahv">Input camera model</param>
        /// <param name="model">Output model in camera space</param>
        /// <param name="toCamera">Transform to camera space</param>
        /// <returns></returns>
        public static bool Decompose(CAHV cahv, out PhotometricModel model, out Matrix toCamera)
        {
            double h_c = cahv.A.Dot(cahv.H);
            double v_c = cahv.A.Dot(cahv.V);
            var principalPoint = new Vector2(h_c, v_c);
            double h_s = cahv.A.Cross(cahv.H).Length();
            double v_s = cahv.A.Cross(cahv.V).Length();
            var focalLengths = new Vector2(h_s, v_s);
            
            Vector3 fwd = cahv.A; fwd.Normalize();
            if (Vector3.Cross(cahv.V, cahv.H).Dot(fwd) < 0)
            {
                fwd = -fwd;
            }

            Vector3 up = cahv.V - v_c * cahv.A; up.Normalize();
            Vector3 right = cahv.H - h_c * cahv.A; right.Normalize();
            Vector3 orthoRight = Vector3.Cross(fwd, up); orthoRight.Normalize();

            Matrix cameraRot = new Matrix();
            for (int i = 0; i < 3; i++)
            {
                cameraRot[i, 0] = orthoRight[i];
                cameraRot[i, 1] = up[i];
                cameraRot[i, 2] = -fwd[i]; 
                cameraRot[i, 3] = 0;
                cameraRot[i, 3] = 0;
            }
            cameraRot[3, 3] = 1;
            toCamera = Matrix.CreateTranslation(-cahv.C) * cameraRot;

            Vector3 cameraRight = Vector3.TransformNormal(right, toCamera);
            double horizAngle = Math.Asin(cameraRight.Y);

            model = new PhotometricModel(focalLengths, principalPoint, horizAngle);
            return true;
        }
    }
}
