using System;
using JPLOPS.Util;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;

namespace JPLOPS.Imaging
{
    public class CameraModelException : Exception
    {
        public CameraModelException() : base() { }
        public CameraModelException(string reason) : base(reason) { }
    }

    public abstract class CameraModel : ICloneable
    {
        protected CameraModel() { }

        /// <summary>
        /// Fast reference based method projecting a ray.
        /// </summary>
        public abstract void Unproject(ref Vector2 pixelPos, out Ray ray);

        /// <summary>
        /// Convience method returns a ray coming out of a camera at a particular pixel position
        /// Should only throw CameraModelException if the math fails.
        /// </summary>
        public Ray Unproject(Vector2 pixelPos)
        {
            Ray r = new Ray();
            Unproject(ref pixelPos, out r);
            return r;
        }

        /// <summary>
        /// Return a 3D position unprojected from the given pixel.
        /// Should only throw CameraModelException if the math fails.
        /// </summary>
        public virtual Vector3 Unproject(Vector2 pixelPos, double range)
        {
            Ray r = Unproject(pixelPos);
            return r.Position + r.Direction * range;
        }

        /// <summary>
        /// Project a 3D position to a pixel location in an image.
        /// Should only throw CameraModelException if the math fails.
        /// </summary>
        public abstract Vector2 Project(Vector3 pos, out double range);

        /// <summary>
        /// Project a 3D position to a pixel location in an image.
        /// Should only throw CameraModelException if the math fails.
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

    /// <summary>
    /// Camera model where pixels correspond to points in a regular grid on a surface.
    /// For OrthographicCameraModel the surface is a plane.
    /// For GISCameraModel the surface is a planetary reference surface (sphere, ellipsoid, or geoid).
    /// </summary>
    public abstract class ConformalCameraModel : CameraModel
    {
        public abstract int Width { get; }
        public abstract int Height { get; }

        public abstract Vector2 MetersPerPixel { get; set; }

        [JsonIgnore]
        public double AvgMetersPerPixel { get { return (MetersPerPixel.X + MetersPerPixel.Y) * 0.5; } }

        [JsonIgnore]
        public double PixelAspect { get { return MetersPerPixel.X / MetersPerPixel.Y; } }

        [JsonIgnore]
        public double WidthMeters { get { return Width * MetersPerPixel.X; } }

        [JsonIgnore]
        public double HeightMeters { get { return Height * MetersPerPixel.Y; } }

        /// <summary>
        /// Goes with Image.Decimated() and DEM.Decimated().
        /// </summary>
        public abstract ConformalCameraModel Decimated(int blocksize);
    }
}
