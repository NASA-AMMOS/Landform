using Emgu.CV;
using Emgu.CV.Structure;
using Microsoft.Xna.Framework;

namespace OPS.Alignment
{
    public class PCA_SIFTFeature : SIFTFeature
    {
        public float GScale;
        public float FScale;
        public int Scale;
        public float SX, SY;
        public Image<Gray, float> Patch;

        public PCA_SIFTFeature(Vector2 location, double size, double angle, int octave, double response, FeatureDescriptor descriptor = null)
            : base(location, size, angle, octave, response, descriptor)
        {
            this.GScale = (float)size;
        }
    }
}

