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

        public override Vector2 Project(Vector3 pos, out double range)
        {
            throw new NotImplementedException();
        }

        public override object Clone()
        {
            throw new NotImplementedException();
        }

        public override void Unproject(ref Vector2 pixelPos, out Ray ray)
        {
            Vector2 metersPerPixel = new Vector2(extent.X / resolution.X, extent.Y / resolution.Y);
            Vector3 origin = invertTransform.Translation;
            // Plus 0.5 for half pixel offset
            origin += invertTransform.Right * metersPerPixel.X * (pixelPos.X + 0.5  - (resolution.X / 2.0));
            origin += invertTransform.Down * metersPerPixel.Y * (pixelPos.Y + 0.5 - (resolution.Y / 2.0));
            ray = new Ray(origin, this.invertTransform.Forward);
        }
    }
}
