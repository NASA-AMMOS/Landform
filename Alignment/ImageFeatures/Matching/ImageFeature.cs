using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Alignment
{
    /// <summary>
    /// A point of interest in an image, optionally with an associated
    /// feature descriptor.
    /// </summary>
    public class ImageFeature
    {
        public Vector2 Location;
        public FeatureDescriptor Descriptor;

        public ImageFeature(Vector2 location, FeatureDescriptor descriptor)
        {
            this.Location = location;
            this.Descriptor = descriptor;
        }
    }
}
