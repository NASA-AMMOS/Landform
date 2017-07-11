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
    /// Interface for classes that can produce images for a mesh
    /// </summary>
    public interface IImageProducer
    {
        /// <summary>
        /// Produce an image for the provided mesh
        /// As a side effect mesh may be re-atlased
        /// </summary>
        /// <param name="mesh">Mesh to generate image for.  Mesh uvs may be modified</param>
        /// <returns></returns>
        Image GenerateImage(Mesh mesh);
    }
}
