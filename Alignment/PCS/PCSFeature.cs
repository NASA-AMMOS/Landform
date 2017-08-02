using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Alignment
{
    public class PCSFeature : ImageFeature
    {
        PCSFeature[] neighbors;

        public PCSFeature(Vector2 location, FeatureDescriptor descriptor = null) : base(location, descriptor)
        {
            neighbors = null;
        }
    }
}
