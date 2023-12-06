using System;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;

namespace JPLOPS.Imaging
{
    public class CAHV : CameraModel 
    {
        public const double EPSILON = 1e-15;

        public Vector3 C;
        public Vector3 A;
        public Vector3 H;
        public Vector3 V;

        [JsonIgnore]
        public override bool Linear { get { return true; } }

        [JsonIgnore]
        public override Vector3 ImagePlaneNormal { get { return A; } }

        public CAHV() { }

        public CAHV(Vector3 c, Vector3 a, Vector3 h, Vector3 v)
        {
            this.C = c;
            this.A = a;
            this.H = h;
            this.V = v;
        }

        public CAHV(CAHV that)
        {
            this.C = that.C;
            this.A = that.A;
            this.H = that.H;
            this.V = that.V;
        }

        /// <summary>
        /// Construct a CAHV camera model with a set of conventional camera parameters.
        /// Port from Mark :-)
        /// </summary>
        /// <param name="imageWidth">number of pixels in the horizontal</param>
        /// <param name="imageHeight">number of pixels in the vertical</param>
        /// <param name="hfov">horizontal field of view (radians)</param>
        /// <param name="vfov">vertical field of view (radians)</param>
        /// <param name="position"> 3D location of the camera</param>
        /// <param name="direction">unit vector of camera pointing direction</param>
        /// <param name="hPrime">unit vector of horizontal image plane direction</param>
        public CAHV(int imageWidth, int imageHeight, double hfov, double vfov, Vector3 position, Vector3 direction, Vector3 hPrime)
        {
            direction = Vector3.Normalize(direction);
            hPrime = Vector3.Normalize(hPrime);
            this.C = position;
            this.A = direction;
            Vector3 vPrime = Vector3.Cross(this.A, hPrime); // ??? reverse?
            //Vector3 vPrime = Vector3.Cross( hPrime,this.A); // ??? reverse?

            double i0 = imageWidth / 2;
            double j0 = imageHeight / 2;
            double fx = i0 / Math.Tan(hfov / 2);
            double fy = j0 / Math.Tan(vfov / 2);

            Vector3 h1 = hPrime * fx;
            Vector3 v1 = vPrime * fy;

            Vector3 h2 = this.A * i0;
            Vector3 v2 = this.A * j0;

            this.H = h1 + h2;
            this.V = v1 + v2;
        }

        

        /// <summary>
        /// Port from Todd's cmod_cahv_2d_to_3d
        /// </summary>
        /// <param name="pixelPos"></param>
        /// <param name="range"></param>
        /// <returns></returns>
        public override void Unproject(ref Vector2 pixelPos, out Ray r)
        {
            Vector3 pos3 = new Vector3();
            Vector3 f = new Vector3();
            Vector3 g = new Vector3();
            Vector3 t = new Vector3();
            double magi;

            Vector3 uvec3 = new Vector3();

            /* The projection point is merely the C of the camera model */
            pos3 = C;                           //copy3(c, pos3);

            /* Calculate the projection ray assuming normal vector directions */
            f = A * pixelPos.Y;                 //scale3(pixelPos[1], A, f);
            f = V - f;                          //sub3(V, f, f);
            g = A * pixelPos.X;                 //scale3(pixelPos[0], A, g);
            g = H - g;                          //sub3(H, g, g);
            uvec3 = Vector3.Cross(f, g);        //cross3(f, g, uvec3);
            magi = uvec3.Length();              //magi = mag3(uvec3);
            if (magi <= EPSILON)
            {
                throw new CameraModelException("divide by zero");
            }
            magi = 1.0 / magi;
            uvec3 = uvec3 * magi;               //scale3(magi, uvec3, uvec3);
            t = Vector3.Cross(V, H);            //cross3(V, H, t);
            
            if (Vector3.Dot(t, A) < 0)          //dot3(t, A)
            {
                uvec3 = uvec3 * -1;             //scale3(-1.0, uvec3, uvec3);
            }
            r.Position = pos3;
            r.Direction = uvec3;

        }

        /// <summary>
        /// Port from Todd's cmod_cahv_3d_to_2d
        /// </summary>
        /// <param name="pos"></param>
        /// <returns></returns>
        public override Vector2 Project(Vector3 pos, out double range)
        {
            Vector3 d = new Vector3();
            double r_1;
            Vector2 pixelPos;
            /* Calculate the projection */
            d = pos - C;                                //sub3(pos3, c, d);
            double dotRange = Vector3.Dot(d, A);
            if (Math.Abs(dotRange) <= EPSILON)
                throw new CameraModelException("divide by zero");
            r_1 = 1.0 / dotRange;
            pixelPos.X = Vector3.Dot(d, H) * r_1;       //pos2[0] = dot3(d, h) * r_1;
            pixelPos.Y = Vector3.Dot(d, V) * r_1;       //pos2[1] = dot3(d, v) * r_1;   
            range = d.Length() * Math.Sign(dotRange);
            return pixelPos;
        }

        public override object Clone()
        {
            return new CAHV(this);
        }
    }
}
