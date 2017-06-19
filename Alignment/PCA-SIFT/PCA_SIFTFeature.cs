using Emgu.CV;
using Emgu.CV.Structure;
using Microsoft.Xna.Framework;

namespace OPS.Alignment
{
    public class PCA_SIFTFeature : ImageFeature
    {
        public double Size;
        public double Angle;
        public int Octave;
        public double Response;
        public float GScale;
        public float FScale;
        public int Scale;
        public float SX, SY;
        public Image<Gray, float> Patch;

        public PCA_SIFTFeature(Vector2 location, double size, double angle, int octave, double response, FeatureDescriptor descriptor = null)
            : base(location, descriptor)
        {
            this.Size = size;
            this.Angle = angle;
            this.Octave = octave;
            this.Response = response;
            this.GScale = (float)size;
            this.Descriptor = descriptor;
        }
    }
}

