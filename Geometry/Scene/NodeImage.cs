using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
