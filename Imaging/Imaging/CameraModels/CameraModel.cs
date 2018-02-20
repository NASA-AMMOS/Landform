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
        public abstract void Backproject2DToRay(ref Vector2 pixelPos, out Ray ray);

        /// <summary>
        /// Convience method returns a ray coming out of a camera at a particular
        /// pixel position
        /// </summary>
        /// <param name="pixelPos"></param>
        /// <returns></returns>
        public Ray Backproject2DToRay(Vector2 pixelPos)
        {
            Ray r = new Ray();
            Backproject2DToRay(ref pixelPos, out r);
            return r;
        }

        /// <summary>
        /// Return a 3D position projected from the given pixel
        /// </summary>
        /// <param name="pixelPos"></param>
        /// <returns></returns>
        public virtual Vector3 Backproject2DTo3D(Vector2 pixelPos, double range)
        {
            Ray r = Backproject2DToRay(pixelPos);
            return r.Position + r.Direction * range;
        }

        /// <summary>
        /// Project a 3d position to a pixel location in an image
        /// </summary>
        /// <param name="pos"></param>
        /// <returns></returns>
        public abstract Vector2 Project3DTo2D(Vector3 pos, out double range);

        /// <summary>
        /// If true, this camera model is purely linear.
        /// </summary>
        public abstract bool Linear { get; }

        public abstract object Clone();
    }
}
