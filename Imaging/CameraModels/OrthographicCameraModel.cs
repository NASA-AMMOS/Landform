using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;

namespace OPS.Imaging
{
    /// <summary>
    /// A basic orthographic camera model
    /// </summary>
    public class OrthographicCameraModel : CameraModel
    {
        private Vector3 center;
        private Vector3 forward;
        private Vector3 right;
        private Vector3 down;
        private Vector3 normal;
        private Vector2 centerPixel;
        private double ff, rr, dd;
        private int width, height;

        public override bool Linear
        {
            get { return true; }
        }

        public override Vector3 ImagePlaneNormal
        {
            get
            {
                return normal;
            }
        }

        public Vector2 MetersPerPixel
        {
            get
            {
                return new Vector2(right.Length(), down.Length());
            }
        }

        /// <summary>
        /// Create an orthograpic camera model.
        /// The vectors forward, right, and down make a basis.
        /// They do not have to be mutually orthogonal or right-handed (but often are).
        /// They also do not have to be unit length.
        /// The length of the right vector defines the horizontal scale of the camera (i.e. the width of a pixel) 
        /// The length of the down vector defines the vertical scale of the camera (i.e. the height of a pixel)
        /// The length of the forward vector defines the range scale of the camera.
        /// </summary>
        /// <param name="center">3D point corresponding pixel location (c, r) = (width, height) * 0.5</param>
        /// <param name="forward">3D vector pointing outward from the camera</param>
        /// <param name="right">3D vector pointing rightwards in the image plane</param>
        /// <param name="down">3D vector pointing downwards in the image plane</param>
        /// <param name="width">number of image columns</param>
        /// <param name="height">number of image rows</param>
        /// <param name="metersPerPixel">scale of image</param>
        /// <returns></returns>
        public OrthographicCameraModel(Vector3 center, Vector3 forward, Vector3 right, Vector3 down,
                                       int width, int height)
        {
            this.center = center;
            this.forward = forward;
            this.right = right;
            this.down = down;
            this.width = width;
            this.height = height;
            Init();
        }

        public OrthographicCameraModel(double r, double l, double t, double b, double far, double near,
                                       int width, int height)
        {
            center = new Vector3((r - l) * 0.5, (t - b) * 0.5, near);
            forward = new Vector3(0, 0, far - near);
            right = new Vector3((r - l) / width, 0, 0);
            down = new Vector3(0, (b - t) / height, 0);
            this.width = width;
            this.height = height;
            Init();
        }

        private void Init()
        {
            normal = Vector3.Normalize(Vector3.Cross(right, down)); //yes this is a right handed cross product
            centerPixel = new Vector2(width, height) * 0.5;
            ff = Vector3.Dot(forward, forward);
            rr = Vector3.Dot(right, right);
            dd = Vector3.Dot(down, down);
        }

        public override Vector2 Project(Vector3 pos, out double range)
        {
            pos -= center;
            range = Vector3.Dot(pos, forward) / ff;
            return new Vector2(Vector3.Dot(pos, right) / rr, Vector3.Dot(pos, down) / dd) + centerPixel;
        }

        public override void Unproject(ref Vector2 pixel, out Ray ray)
        {
            pixel -= centerPixel;
            ray = new Ray(center + right * pixel.X + down * pixel.Y, forward);
        }

        public override object Clone()
        {
            return (OrthographicCameraModel) MemberwiseClone();
        }
    }
}
