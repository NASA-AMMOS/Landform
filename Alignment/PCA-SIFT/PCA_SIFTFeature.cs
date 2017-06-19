using Microsoft.Xna.Framework;

namespace OPS.Alignment
{
    public class PCA_SIFTFeature : ImageFeature
    {
        public double Size;
        public double Angle;
        public int Octave;
        public double Response;

        public PCA_SIFTFeature(Vector2 location, double size, double angle, int octave, double response, FeatureDescriptor descriptor = null)
            : base(location, descriptor)
        {
            this.Size = size;
            this.Angle = angle;
            this.Octave = octave;
            this.Response = response; // what to do with descriptor?
        }

    }
}

