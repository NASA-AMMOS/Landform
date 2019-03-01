using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Drawing;
using Microsoft.Xna.Framework;
using Emgu.CV;
using Emgu.CV.Structure;

namespace OPS.Alignment
{
    public class SIFTFeature : ImageFeature
    {
        public double Size;
        public double Angle;
        public int Octave;
        public double Response;

        public SIFTFeature(Vector2 location, double size, double angle, int octave, double response, FeatureDescriptor descriptor = null)
            : base(location, descriptor)
        {
            this.Size = size;
            this.Angle = angle;
            this.Octave = octave;
            this.Response = response;
        }

        public static explicit operator MKeyPoint(SIFTFeature feature)
        {
            return new MKeyPoint() {
                Angle = (float)feature.Angle,
                Octave = feature.Octave,
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
