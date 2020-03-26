using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;

namespace OPS.Imaging
{
    public abstract class CameraModel : ICloneable
    {
        protected CameraModel() { }

        /// <summary>
        /// Fast reference based method projecting a ray.  Camera model classes
        /// implement this method
        /// </summary>
        /// <param name="pixelPos"></param>
        /// <param name="ray"></param>
        /// <returns></returns>
        public abstract void Unproject(ref Vector2 pixelPos, out Ray ray);

        /// <summary>
        /// Convience method returns a ray coming out of a camera at a particular
        /// pixel position
        /// </summary>
        /// <param name="pixelPos"></param>
        /// <returns></returns>
        public Ray Unproject(Vector2 pixelPos)
        {
            Ray r = new Ray();
            Unproject(ref pixelPos, out r);
            return r;
        }

        /// <summary>
        /// Return a 3D position unprojected from the given pixel
        /// </summary>
        /// <param name="pixelPos"></param>
        /// <returns></returns>
        public virtual Vector3 Unproject(Vector2 pixelPos, double range)
        {
            Ray r = Unproject(pixelPos);
            return r.Position + r.Direction * range;
        }

        /// <summary>
        /// Project a 3D position to a pixel location in an image
        /// </summary>
        /// <param name="pos"></param>
        /// <returns></returns>
        public abstract Vector2 Project(Vector3 pos, out double range);

        public Vector2 Project(Vector3 pos)
        {
            return Project(pos, out double range);
        }

        /// <summary>
        /// If true, this camera model is purely linear.
        /// </summary>
        public abstract bool Linear { get; }

        public abstract object Clone();

        /// <summary>
        /// the direction normal to the image plane and pointing outward.
        /// This is not necessarily the direction through the middle pixel of your image.
        /// </summary>
        public abstract Vector3 ImagePlaneNormal { get; }
    }
}
