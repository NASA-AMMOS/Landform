using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Alignment
{
    public class Feature
    {
        public Vector2 location;
        public double size;
        public double angle;
        public float[] descriptor;

        public Feature(Vector2 location, double size, double angle, float[] descriptor = null)
        {
            this.location = location;
            this.size = size;
            this.angle = angle;
            this.descriptor = descriptor;
        }
    }
}
