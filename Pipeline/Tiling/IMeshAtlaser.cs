using OPS.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Pipeline
{
    /// <summary>
    /// Interface for generating atlases for a mesh
    /// </summary>
    public interface IMeshAtlaser
    {
        /// <summary>
        /// Returns a copy of this mesh with UV corrdinates
        /// </summary>
        /// <param name="mesh"></param>
        /// <returns></returns>
        Mesh GenerateAtlas(Mesh mesh);
    }
}
