using System;
using System.Linq;
using Microsoft.Xna.Framework;

namespace JPLOPS.Geometry
{

    public class RigidTransform2D
    {
        public Vector2 Translation;
        
        private double theta, cos, sin;
        public double Rotation //radians
        {
            set
            {
                theta = value;
                cos = Math.Cos(value);
                sin = Math.Sin(value);
            }
            
            get
            {
                return theta;
            }
        }

        public RigidTransform2D()
        {
            this.Translation = Vector2.Zero;
            this.Rotation = 0;
        }

        public RigidTransform2D(Vector2 translation, double rotation)
        {
            this.Translation = translation;
            this.Rotation = rotation;
        }
        
        public Vector2 Transform(Vector2 pt)
        {
            return Translate(Rotate(pt));
        }

        public Vector2 Rotate(Vector2 pt)
        {
            return new Vector2(cos * pt.X - sin * pt.Y, sin * pt.X + cos * pt.Y);
        }

        public Vector2 Translate(Vector2 pt)
        {
            return Translation + pt;
        }
        
        public static RigidTransform2D Estimate(Vector2[] movingPts, Vector2[] fixedPts, out double residual)
        {
            if (movingPts.Length != fixedPts.Length)
            {
                throw new ArgumentException("must have equal numbers of points to estimate transform");
            }
            
            residual = 0;
            
            int n = movingPts.Length;
            if (n == 0)
            {
                return new RigidTransform2D();
            }
            else if (n == 1)
            {
                return new RigidTransform2D(fixedPts[0] - movingPts[0], 0);
            }
            else if (n == 2)
            {
                var avgFixed = 0.5 * (fixedPts[0] + fixedPts[1]);
                var avgMoving = 0.5 * (movingPts[0] + movingPts[1]);
                var translation = avgFixed - avgMoving;
                
                var fixedVec = fixedPts[1] - fixedPts[0];
                var movingVec = movingPts[1] - movingPts[0];
                var fixedVecLengthSquared = fixedVec.LengthSquared();
                var movingVecLengthSquared = movingVec.LengthSquared();
                double rotation = 0;
                if (fixedVecLengthSquared > 1e-6 && movingVecLengthSquared > 1e-6)
                {
                    var fv3D = new Vector3(fixedVec.X, fixedVec.Y, 0) * (1 / Math.Sqrt(fixedVecLengthSquared));
                    var mv3D = new Vector3(movingVec.X, movingVec.Y, 0) * (1 / Math.Sqrt(movingVecLengthSquared));
                    double sin = Vector3.Cross(mv3D, fv3D).Z;
                    double cos = Vector3.Dot(mv3D, fv3D);
                    rotation = Math.Atan2(sin, cos);
                }
                var ret = new RigidTransform2D(translation, rotation);
                residual = ret.Residual(movingPts, fixedPts);
                return ret;
            }
            else
            {
                residual = Procrustes.Calculate(movingPts.Select(p => new Vector3(p.X, p.Y, 0)).ToArray(),
                                                fixedPts.Select(p => new Vector3(p.X, p.Y, 0)).ToArray(),
                                                out Vector3 translation, out Quaternion quat, out double scale,
                                                calcTranslation: true, calcRotation: true, calcScale: false);
                double cos = quat.W;
                double sin = new Vector3(quat.X, quat.Y, quat.Z).Length();
                return new RigidTransform2D(new Vector2(translation.X, translation.Y), 2 * Math.Atan2(sin, cos));
            }
        }
        
        public double Residual(Vector2[] movingPts, Vector2[] fixedPts)
        {
            if (movingPts.Length != fixedPts.Length)
            {
                throw new ArgumentException("must have equal numbers of points to calculate residual");
            }
            int n = movingPts.Length;
            double ret = 0;
            for (int i = 0; i < n; i++)
            {
                
                ret += Vector2.DistanceSquared(Transform(movingPts[i]), fixedPts[i]);
            }
            return Math.Sqrt(ret / n);
        }
    }
}
