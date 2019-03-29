using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Alignment
{
    public class ImageFeature
    {
        public double Range = 0; //positive iff feature has valid associated range
        public Vector2 Location;
        public FeatureDescriptor Descriptor;

        //needed for JSON deserialization
        public ImageFeature() { }

        public ImageFeature(Vector2 location, FeatureDescriptor descriptor)
        {
            this.Location = location;
            this.Descriptor = descriptor;
        }
    }
}
