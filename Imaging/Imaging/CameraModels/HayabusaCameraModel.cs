using System;
using System.Xml;
using Microsoft.Xna.Framework;

namespace OPS.Imaging
{
    public class HayabusaCameraModel : CameraModel
    {
        public override bool Linear => false;

        /// <summary>
        /// Focal length, in meters
        /// </summary>
        public readonly double FocalLength;
        /// <summary>
        /// Distortion parameter
        /// </summary>
        public readonly double K;
        /// <summary>
        /// Scaling factor applied to the image (1, 1/2, 1/4, etc.)
        /// </summary>
        public readonly double Scale;
        private static readonly int MAX_ITERS = 100;

        public HayabusaCameraModel(double focalLength, double k, double scale)
        {
            FocalLength = focalLength;
            K = k;
            Scale = scale;
        }

        public override object Clone()
        {
            return new HayabusaCameraModel(FocalLength, K, Scale);
        }

        int NonZeroSign(double x)
        {
            if (x < 0) return -1;
            return 1;
        }

        public override Vector2 Project(Vector3 pos, out double range)
        {
            range = pos.Length() * NonZeroSign(pos.Z);
            Vector2 undistorted = FocalLength * new Vector2(pos.X / pos.Z, pos.Y / pos.Z);
            double r2 = undistorted.LengthSquared();
            return (undistorted * (1 + K * r2) + new Vector2(512, 512)) * Scale;
        }

        public override void Unproject(ref Vector2 pixelPos, out Ray ray)
        {
            Vector2 distorted = (pixelPos / Scale) - new Vector2(512, 512);
            double rPrime = distorted.Length();
            double r = rPrime;
            if (K != 0)
            {
                for (int i = 0; i < MAX_ITERS; i++)
                {
                    var err = rPrime - r * (1 + K * r * r);
                    if (Math.Abs(err) < 1e-7) break;

                    var dErr = 1 + 3 * K * r * r;
                    r = r - err / dErr;
                }
            }
            Vector2 undistorted = distorted * r / rPrime;

            ray.Position = Vector3.Zero;
            ray.Direction = new Vector3(undistorted.X / FocalLength, undistorted.Y / FocalLength, 1);
            ray.Direction.Normalize();
        }
    }
}
