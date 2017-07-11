using Microsoft.Xna.Framework;
using OPS.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Pipeline
{
    /// <summary>
    /// Interface for objects that can determine when a mesh should be split when tiling
    /// </summary>
    public interface ITileSplitCriteria
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="meshOperator">Operator with source mesh to consider splitting</param>
        /// <param name="bounds">Bounding area to consider splitting</param>
        /// <returns>True if this boudnding area should be subdevided</returns>
        bool ShouldSplit(MeshOperator meshOperator, BoundingBox bounds);
    }
}
