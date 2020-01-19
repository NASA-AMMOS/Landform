using System;
using System.Linq;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using OPS.Imaging;

namespace OPS.Geometry
{
    public class NodeImage : NodeComponent
    {
        public CameraModel CameraModel;
        public Vector2? Size;
        public string Url;
    }
}
