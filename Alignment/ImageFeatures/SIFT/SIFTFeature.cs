using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
        
    }
}
