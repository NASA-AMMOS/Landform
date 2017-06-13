using Emgu.CV;
using Emgu.CV.Structure;
using System.Diagnostics;

namespace OPS.Alignment
{
    public class PCA_Keypoint
    {
        // not sure if this needs to be done
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
        public float[] ld { get; set; }       
        public Image<Gray, float> patch { get; set; }

        public PCA_Keypoint(MKeyPoint point)
        {
            GScale = point.Size;
            Angle = point.Angle;
            Response = point.Response;
            X = point.Point.X;
            Y = point.Point.Y;
            SX = point.Point.X;
            SY = point.Point.Y;
            ld = new float[EPCALEN];
            unpackOctaveAndScale(point.Octave);
        }

        public PCA_Keypoint()
        {

        }

        void unpackOctaveAndScale(int octave)
        {
            octave = octave & 255;
            Octave = octave < 128 ? octave : (-128 | octave) + 1;
            Scale = Octave; // I don't think this is right
        }
    }
}