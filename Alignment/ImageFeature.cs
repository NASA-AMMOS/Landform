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
        public Vector2 Location;
        public double Size;
        public double Angle;
        public float[] Descriptor;

        public ImageFeature(Vector2 location, double size, double angle, float[] descriptor = null)
        {
            this.Location = location;
            this.Size = size;
            this.Angle = angle;
            this.Descriptor = descriptor;
        }
    }
}
