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
        private Matrix transform;
        private Vector2 resolution;
        private Vector2 extent;
        private Matrix invertTransform;
        private Vector3 C;
        private Vector3 A;
        private Vector3 H;
        private Vector3 V;

        public override bool Linear
        {
            get { return true; }
        }

        public override Vector3 ImagePlaneNormal
        {
            get
            {
                throw new NotImplementedException();
            }
        }

        /// <summary>
        /// Create a camera at the location and orientation specified by transform
        /// Use the XY pixel resolution
        /// verticalExtent is the size in meters along the Y pixel axis of the camera.  The horiziontal extent will be calcualted accordingly
        /// </summary>
        /// <param name="transform"></param>
        /// <param name="resolution"></param>
        /// <param name="verticleExtent"></param>
        public OrthographicCameraModel(Matrix transform, Vector2 resolution, double verticleExtent)
        {
            this.transform = transform;
            this.invertTransform = Matrix.Invert(transform);
            this.resolution = resolution;
            double metersPerPixel = verticleExtent / resolution.Y;
            this.extent = new Vector2(metersPerPixel*resolution.X, verticleExtent);
            this.C = new Vector3(invertTransform.M41, invertTransform.M42, invertTransform.M43);
            this.A = -1.0 * new Vector3(invertTransform.M31, invertTransform.M32, invertTransform.M33);
            this.H = new Vector3(invertTransform.M11, invertTransform.M12, invertTransform.M13);
            this.V = new Vector3(invertTransform.M21, invertTransform.M22, invertTransform.M23);
            this.A.Normalize();
            this.H.Normalize();
            this.V.Normalize();      
        }

        public OrthographicCameraModel(Matrix transform, double width, double height, double metersPerPixel)
        {
            this.transform = transform;
            this.invertTransform = Matrix.Invert(transform);
            this.resolution = new Vector2(width, height);
            this.extent = new Vector2(metersPerPixel * resolution.X, metersPerPixel * resolution.Y);
            this.C = new Vector3(invertTransform.M41, invertTransform.M42, invertTransform.M43);
            this.A = -1.0 * new Vector3(invertTransform.M31, invertTransform.M32, invertTransform.M33);
            this.H = new Vector3(invertTransform.M11, invertTransform.M12, invertTransform.M13);
            this.V = new Vector3(invertTransform.M21, invertTransform.M22, invertTransform.M23);
            this.A.Normalize();
            this.H.Normalize();
            this.V.Normalize();
        }

        /// <summary>
        /// Similar to the other constructor but allows you to control the extent in both the X and Y pixel directions
        /// </summary>
        /// <param name="transform"></param>
        /// <param name="resolution"></param>
        /// <param name="extent"></param>
        public OrthographicCameraModel(Matrix transform, Vector2 resolution, Vector2 extent)
        {
            this.transform = transform;
            this.invertTransform = Matrix.Invert(transform);
            this.resolution = resolution;
            this.extent = extent;
            this.C = new Vector3(invertTransform.M41, invertTransform.M42, invertTransform.M43);
            this.A = -1.0 * new Vector3(invertTransform.M31, invertTransform.M32, invertTransform.M33);
            this.H = new Vector3(invertTransform.M11, invertTransform.M12, invertTransform.M13);
            this.V = new Vector3(invertTransform.M21, invertTransform.M22, invertTransform.M23);
            this.A.Normalize();
            this.H.Normalize();
            this.V.Normalize();
        }

        /// <summary>
        /// Allows passing in basis vectors directly
        /// </summary>
        /// <param name="pos"></param>
        /// <param name="range"></param>
        /// <returns></returns>
        public OrthographicCameraModel(Vector3 C, Vector3 A, Vector3 H, Vector3 V, Vector2 resolution, Vector2 extent)
        {
            this.resolution = resolution;
            this.extent = extent;
            this.C = C;
            this.A = A;
            this.H = H;
            this.V = V;
            this.A.Normalize();
            this.H.Normalize();
            this.V.Normalize();
        }

        //TODO: Do we need to normalize axis vecetors here?
        public override Vector2 Project(Vector3 pos, out double range)
        {
            // Needs validation / review
            Vector2 metersPerPixel = new Vector2(extent.X / resolution.X, extent.Y / resolution.Y);
            Vector3 origin = C;
            Vector3 offset = pos - origin;
            var pixelPos = Vector2.Zero;
            double h_offset_meters = Vector3.Dot(H, offset);
            double v_offset_meters = -1.0 * Vector3.Dot(V, offset); //Vertical flip as pixel row increases downwards
            pixelPos.X = h_offset_meters / metersPerPixel.X + resolution.X / 2 - 0.5;
            pixelPos.Y = v_offset_meters / metersPerPixel.Y + resolution.Y / 2 - 0.5;
            range = Vector3.Dot(offset, A);
            return pixelPos;
        }

        public override object Clone()
        {
            throw new NotImplementedException();
        }

        public override void Unproject(ref Vector2 pixelPos, out Ray ray)
        {
            Vector2 metersPerPixel = new Vector2(extent.X / resolution.X, extent.Y / resolution.Y);
            Vector3 origin = C;
            // Plus 0.5 for half pixel offset
            origin += H * metersPerPixel.X * (pixelPos.X + 0.5  - (resolution.X / 2.0));
            origin += -1.0 * V * metersPerPixel.Y * (pixelPos.Y + 0.5 - (resolution.Y / 2.0)); //Vertical flip as pixel row increases downwards
            ray = new Ray(origin, A);
        }
    }
}
