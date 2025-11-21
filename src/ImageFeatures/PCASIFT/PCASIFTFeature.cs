using Microsoft.Xna.Framework;
using Emgu.CV;
using Emgu.CV.Structure;

namespace JPLOPS.ImageFeatures
{
    public class PCASIFTFeature : SIFTFeature
    {
        public float GScale;
        public float FScale;
        public int IScale;
        public float SX, SY;
        public Image<Gray, float> Patch;

        //needed for JSON deserialization
        public PCASIFTFeature() { }

        public PCASIFTFeature(Vector2 location, double size, double angle, int octave, double response,
                              FeatureDescriptor descriptor = null)
            : base(location, size, angle, octave, response, descriptor)
        {
            GScale = (float)size;
        }

        public PCASIFTFeature(MKeyPoint kp, FeatureDescriptor descriptor = null)
            : this(new Vector2(kp.Point.X, kp.Point.Y), kp.Size, kp.Angle, kp.Octave, kp.Response)
        { }
    }
}

