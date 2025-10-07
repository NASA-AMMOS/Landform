using Microsoft.Xna.Framework;
using Newtonsoft.Json;

namespace JPLOPS.Imaging
{
    /// <summary>
    /// A basic orthographic camera model
    /// </summary>
    public class OrthographicCameraModel : ConformalCameraModel
    {
        public Vector3 Center { get; private set; }
        public Vector3 Forward { get; private set; }
        public Vector3 Right { get; private set; }
        public Vector3 Down { get; private set; }

        public override int Width { get { return width; } }
        public override int Height { get { return height; } }

        [JsonIgnore]
        public Vector2 CenterPixel { get; private set; }

        [JsonIgnore]
        public override bool Linear { get { return true; } }

        [JsonIgnore]
        public override Vector3 ImagePlaneNormal { get { return normal; } }

        [JsonIgnore]
        public override Vector2 MetersPerPixel
        {
            get
            {
                return new Vector2(Right.Length(), Down.Length());
            }

            set
            {
                Right = Vector3.Normalize(Right) * value.X;
                Down = Vector3.Normalize(Down) * value.Y;
                Init();
            }
        }

        private int width, height;
        private Vector3 normal;
        private double ff, rr, dd;

        /// <summary>
        /// Create an orthograpic camera model.
        /// The vectors forward, right, and down make a basis.
        /// They do not have to be mutually orthogonal or right-handed (but often are).
        /// They also do not have to be unit length.
        /// The length of the right vector defines the horizontal scale of the camera (i.e. the width of a pixel) 
        /// The length of the down vector defines the vertical scale of the camera (i.e. the height of a pixel)
        /// The length of the forward vector defines the range scale of the camera.
        /// </summary>
        /// <param name="center">3D point corresponding pixel location (c, r) = (width - 1, height - 1) * 0.5</param>
        /// <param name="forward">3D vector pointing outward from the camera</param>
        /// <param name="right">3D vector pointing rightwards in the image plane</param>
        /// <param name="down">3D vector pointing downwards in the image plane</param>
        /// <param name="width">number of image columns</param>
        /// <param name="height">number of image rows</param>
        /// <param name="metersPerPixel">scale of image</param>
        /// <returns></returns>
        [JsonConstructor]
        public OrthographicCameraModel(Vector3 center, Vector3 forward, Vector3 right, Vector3 down,
                                       int width, int height)
        {
            this.Center = center;
            this.Forward = forward;
            this.Right = right;
            this.Down = down;
            this.width = width;
            this.height = height;
            Init();
        }

        public OrthographicCameraModel(double r, double l, double t, double b, double far, double near,
                                       int width, int height)
        {
            Center = new Vector3((r - l) * 0.5, (t - b) * 0.5, near);
            Forward = new Vector3(0, 0, far - near);
            Right = new Vector3((r - l) / width, 0, 0);
            Down = new Vector3(0, (b - t) / height, 0);
            this.width = width;
            this.height = height;
            Init();
        }

        private void Init()
        {
            normal = Vector3.Normalize(Vector3.Cross(Right, Down)); //yes this is a right handed cross product
            CenterPixel = new Vector2(Width - 1, Height - 1) * 0.5;
            ff = Vector3.Dot(Forward, Forward);
            rr = Vector3.Dot(Right, Right);
            dd = Vector3.Dot(Down, Down);
        }

        public override Vector2 Project(Vector3 point, out double range)
        {
            //NOTE: c = Vector3.Dot(a, b) / Vector3.Dot(b, b)
            //computes c as component of a in direction of b measured in units of ||b||
            var ctrToPt = point - Center;
            range = Vector3.Dot(ctrToPt, Forward) / ff;
            return new Vector2(Vector3.Dot(ctrToPt, Right) / rr, Vector3.Dot(ctrToPt, Down) / dd) + CenterPixel;
        }

        public override void Unproject(ref Vector2 pixel, out Ray ray)
        {
            pixel -= CenterPixel;
            ray = new Ray(Center + Right * pixel.X + Down * pixel.Y, Forward);
        }

        public override object Clone()
        {
            return (OrthographicCameraModel) MemberwiseClone();
        }

        public override ConformalCameraModel Decimated(int blocksize)
        {
            return new OrthographicCameraModel(Center, Forward, Right * blocksize, Down * blocksize,
                                               Width / blocksize, Height / blocksize);
        }
    }
}
