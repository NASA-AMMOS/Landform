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
        protected CameraModel()
        {

        }

        /// <summary>
        /// Fast reference based method projecting a ray.  Camera model classes
        /// implement this method
        /// </summary>
        /// <param name="pixelPos"></param>
        /// <param name="ray"></param>
        /// <returns></returns>
        public abstract void ProjectRay(ref Vector2 pixelPos, out Ray ray);

        /// <summary>
        /// Convience method returns a ray coming out of a camera at a particular
        /// pixel position
        /// </summary>
        /// <param name="pixelPos"></param>
        /// <returns></returns>
        public Ray ProjectRay(Vector2 pixelPos)
        {
            Ray r = new Ray();
            ProjectRay(ref pixelPos, out r);
            return r;
        }

        /// <summary>
        /// Return a position projected from the given pixel
        /// </summary>
        /// <param name="pixelPos"></param>
        /// <returns></returns>
        public virtual Vector3 ProjectPoint(Vector2 pixelPos, double range)
        {
            Ray r = ProjectRay(pixelPos);
            return r.Position + r.Direction * range;
        }

        /// <summary>
        /// Backproject a 3d position to a pixel location in an image
        /// </summary>
        /// <param name="pos"></param>
        /// <returns></returns>
        public abstract Vector2 Backproject(Vector3 pos, out double range);

        /// <summary>
        /// If true, this camera model is purely linear.
        /// </summary>
        public abstract bool Linear { get; }

        public abstract object Clone();
    }
}
