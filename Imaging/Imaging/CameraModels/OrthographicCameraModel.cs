using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;

namespace OPS.Imaging
{
    public class OrthographicCameraModel : CameraModel
    {
        private Matrix transform;
        private Vector2 resolution;
        private Vector2 extent;
        private Matrix invertTransform;

        public OrthographicCameraModel(Matrix transform, Vector2 resolution, double verticleExtent)
        {
            this.transform = transform;
            this.invertTransform = Matrix.Invert(transform);
            this.resolution = resolution;
            double metersPerPixel = verticleExtent / resolution.Y;
            this.extent = new Vector2(metersPerPixel*resolution.X, verticleExtent);
        }

        public OrthographicCameraModel(Matrix transform, Vector2 resolution, Vector2 extent)
        {
            this.transform = transform;
            this.invertTransform = Matrix.Invert(transform);
            this.resolution = resolution;
            this.extent = extent;
        }

        public override Vector2 Backproject(Vector3 pos, out double range)
        {
            throw new NotImplementedException();
        }

        public override object Clone()
        {
            throw new NotImplementedException();
        }

        public override void ProjectRay(ref Vector2 pixelPos, out Ray ray)
        {
            Vector2 metersPerPixel = new Vector2(extent.X / resolution.X, extent.Y / resolution.Y);
            Vector3 origin = invertTransform.Translation;
            origin += invertTransform.Right * metersPerPixel.X * (pixelPos.X - (resolution.X / 2.0));
            origin += invertTransform.Down * metersPerPixel.Y * (pixelPos.Y - (resolution.Y / 2.0));
            ray = new Ray(origin, this.invertTransform.Forward);
        }
    }
}
