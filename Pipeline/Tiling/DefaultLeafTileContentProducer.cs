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
    /// Default tile producer for clipping mesh tiles and making images
    /// </summary>
    public class DefaultLeafTileContentProducer : IContentProducer
    {
        IImageProducer imageProducer;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="imageProducer">Image producer to use when generating images for tiles</param>
        public DefaultLeafTileContentProducer(IImageProducer imageProducer = null)
        {
            this.imageProducer = imageProducer;
        }

        /// <summary>
        /// Will produce content and add content for/to this node
        /// </summary>
        /// <param name="curNode"></param>
        /// <param name="meshOperator">Operator to use when generating mesh</param>
        public void ProduceContent(SceneNode curNode, MeshOperator meshOperator)
        {
            Mesh mesh = meshOperator.Clip(curNode.Bounds);
            Image image = null;
            if (imageProducer != null)
            {
                image = imageProducer.GenerateImage(mesh);
            }
            MeshImagePair pair = curNode.AddComponent<MeshImagePair>();
            pair.Mesh = mesh;
            pair.Image = image;
        }
    }

}
