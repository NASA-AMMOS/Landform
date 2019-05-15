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
        }

        public OrthographicCameraModel(Matrix transform, double width, double height, double metersPerPixel)
        {
            this.transform = transform;
            this.invertTransform = Matrix.Invert(transform);
            this.resolution = new Vector2(width, height);
            this.extent = new Vector2(metersPerPixel * resolution.X, metersPerPixel * resolution.Y);
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
        }

        //TODO: Do we need to normalize axis vecetors here?
        public override Vector2 Project(Vector3 pos, out double range)
        {
            // Needs validation / review
            Vector2 metersPerPixel = new Vector2(extent.X / resolution.X, extent.Y / resolution.Y);
            Vector3 origin = invertTransform.Translation;
            Vector3 offset = pos - origin;
            var pixelPos = Vector2.Zero;
            Vector3 horizontal_axis = new Vector3(invertTransform.M11, invertTransform.M12, invertTransform.M13);
            Vector3 vertical_axis = new Vector3(invertTransform.M21, invertTransform.M22, invertTransform.M23);
            horizontal_axis.Normalize();
            vertical_axis.Normalize();
            double h_offset_meters = Vector3.Dot(horizontal_axis, offset);
            double v_offset_meters = -1.0 * Vector3.Dot(vertical_axis, offset); //Vertical flip as pixel row increases downwards
            pixelPos.X = h_offset_meters / metersPerPixel.X + resolution.X / 2 - 0.5;
            pixelPos.Y = v_offset_meters / metersPerPixel.Y + resolution.Y / 2 - 0.5;
            range = Vector3.Dot(offset, invertTransform.Forward) / invertTransform.Forward.Length();
            return pixelPos;
        }

        public override object Clone()
        {
            throw new NotImplementedException();
        }

        public override void Unproject(ref Vector2 pixelPos, out Ray ray)
        {
            Vector2 metersPerPixel = new Vector2(extent.X / resolution.X, extent.Y / resolution.Y);
            Vector3 origin = invertTransform.Translation;
            Vector3 horizontal_axis = new Vector3(invertTransform.M11, invertTransform.M12, invertTransform.M13);
            Vector3 vertical_axis = new Vector3(invertTransform.M21, invertTransform.M22, invertTransform.M23);
            horizontal_axis.Normalize();
            vertical_axis.Normalize();
            // Plus 0.5 for half pixel offset
            origin += horizontal_axis * metersPerPixel.X * (pixelPos.X + 0.5  - (resolution.X / 2.0));
            origin += -1.0 * vertical_axis * metersPerPixel.Y * (pixelPos.Y + 0.5 - (resolution.Y / 2.0)); //Vertical flip as pixel row increases downwards
            ray = new Ray(origin, this.invertTransform.Forward / this.invertTransform.Forward.Length());
        }
    }
}
