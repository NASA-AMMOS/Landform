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

        public MeshImagePair()
        {

        }

        public MeshImagePair(Mesh mesh = null, Image image = null)
        {
            this.Mesh = mesh;
            this.Image = image;
        }
    }

    /// <summary>
    /// Container for adding an index image to a scene node
    /// </summary>
    public class IndexImage : NodeComponent
    {
        public Image Index;
        public IndexImage()
        {

        }
        public IndexImage(Image index)
        {
            if(index == null)
            {
                throw new Exception("Index image cannot be null");
            }
            this.Index = index;
        }
    }
}
