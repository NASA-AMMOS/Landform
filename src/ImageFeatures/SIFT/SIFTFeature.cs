using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using Microsoft.Xna.Framework;
using Emgu.CV.Structure;

namespace JPLOPS.ImageFeatures
{
    public class SIFTFeature : ImageFeature
    {
        public double Size; //neighborhood diameter
        public double Angle; //degrees in [0, 360)
        public int PackedOctave;
        public int Octave;
        public int Layer;
        public float Scale;
        public double Response;

        //needed for JSON deserialization
        public SIFTFeature() { }

        public SIFTFeature(Vector2 location, double size, double angle, int octave, double response,
                           FeatureDescriptor descriptor = null)
            : base(location, descriptor)
        {
            this.Size = size;
            this.Angle = angle;

            //https://github.com/opencv/opencv/issues/4554
            this.PackedOctave = octave;
            this.Layer = (octave >> 8) & 255;
            octave = octave & 255;
            this.Octave = octave = octave < 128 ? octave : (-128 | octave);
            this.Scale = octave >= 0 ? 1.0f/(1 << octave) : (float)(1 << -octave);

            this.Response = response;
        }

        public SIFTFeature(MKeyPoint kp, FeatureDescriptor descriptor = null)
            : this(new Vector2(kp.Point.X, kp.Point.Y), kp.Size, kp.Angle, kp.Octave, kp.Response, descriptor)
        { }

        public SIFTFeature(SIFTFeature other) : base(other.Location, other.Descriptor)
        {
            this.Size = other.Size;
            this.Angle = other.Angle;
            this.PackedOctave = other.PackedOctave;
            this.Layer = other.Layer;
            this.Octave = other.Octave;
            this.Scale = other.Scale;
            this.Response = other.Response;
        }

        public static explicit operator MKeyPoint(SIFTFeature feature)
        {
            return new MKeyPoint() {
                Angle = (float)feature.Angle,
                Octave = feature.PackedOctave,
                Point = new PointF((float)feature.Location.X, (float)feature.Location.Y),
                Response = (float)feature.Response,
                Size = (float)feature.Size
            };
        }
    }
        
    public static class SIFTFeatureExtensions
    {
        //can't use Cast<MKeyPoint>() unfortunately because linq Cast<>() doesn't work with user defined conversions
        public static IEnumerable<MKeyPoint> CastToMKeyPoint(this IEnumerable<SIFTFeature> features)
        {
            return features.Select(f => (MKeyPoint)f);
        }
    }
}
