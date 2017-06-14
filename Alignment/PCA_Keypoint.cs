using Emgu.CV;
using Emgu.CV.Structure;
using System.Diagnostics;

namespace OPS.Alignment
{
    public class PCA_Keypoint
    {
        public float GScale { get; set; }
        public float FScale { get; set; }
        public int Scale { get; set; }
        public float Angle { get; set; }
        public float Response { get; set; }
        public int Octave { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float SX { get; set; }
        public float SY { get; set; }
        const int EPCALEN = 36;
        public float[] desc { get; set; }       
        public Image<Gray, float> Patch { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="T:OPS.Alignment.PCA_Keypoint"/> class.
        /// </summary>
        /// <param name="point">Point.</param>
        public PCA_Keypoint(MKeyPoint point)
        {
            GScale = point.Size;
            Angle = point.Angle;
            Response = point.Response;
            X = point.Point.X;
            Y = point.Point.Y;
            SX = point.Point.X;
            SY = point.Point.Y;
            desc = new float[EPCALEN];
            UnpackOctaveAndScale(point.Octave);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="T:OPS.Alignment.PCA_Keypoint"/> class.
        /// </summary>
        public PCA_Keypoint() { }

        /// <summary>
        /// Unpacks the octave and scale from SIFT-detected keypoints.
        /// </summary>
        /// <param name="octave">Octave.</param>
        void UnpackOctaveAndScale(int octave)
        {
            octave = octave & 255;
            Octave = octave < 128 ? octave : (-128 | octave) + 1;
            Scale = Octave; // I don't think this is right
        }
    }
}