using OPS.Geometry;
using OPS.Imaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Pipeline
{
    /// <summary>
    /// Content container for adding mesh and image data to a scene node
    /// </summary>
    public class MeshImagePair : NodeComponent
    {
        public Mesh Mesh;
        public Image Image;
        public Image Index;

        public MeshImagePair()
        {

        }

        public MeshImagePair(Mesh mesh = null, Image image = null, Image index = null)
        {
            this.Mesh = mesh;
            this.Image = image;
            this.Index = index;
        }
    }
}
