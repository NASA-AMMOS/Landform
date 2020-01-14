using System;
using System.Collections.Generic;
using System.Linq;
using OPS.Util;
using Microsoft.Xna.Framework;

namespace OPS.Imaging
{
    public abstract class CameraModel : ICloneable
    {
        protected CameraModel() { }

        /// <summary>
        /// Fast reference based method projecting a ray.
        /// </summary>
        public abstract void Unproject(ref Vector2 pixelPos, out Ray ray);

        /// <summary>
        /// Convience method returns a ray coming out of a camera at a particular pixel position
        /// </summary>
        public Ray Unproject(Vector2 pixelPos)
        {
            Ray r = new Ray();
            Unproject(ref pixelPos, out r);
            return r;
        }

        /// <summary>
        /// Return a 3D position unprojected from the given pixel.
        /// </summary>
        public virtual Vector3 Unproject(Vector2 pixelPos, double range)
        {
            Ray r = Unproject(pixelPos);
            return r.Position + r.Direction * range;
        }

        /// <summary>
        /// Project a 3D position to a pixel location in an image.
        /// </summary>
        public abstract Vector2 Project(Vector3 pos, out double range);

        /// <summary>
        /// Project a 3D position to a pixel location in an image.
        /// </summary>
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

        public string Serialize()
        {
            return JsonHelper.ToJson(this);
        }

        public static CameraModel Deserialize(string str)
        {
            return (CameraModel)JsonHelper.FromJson(str);
        }
    }
}
