using Microsoft.Xna.Framework;
using JPLOPS.Geometry;

namespace JPLOPS.Pipeline
{
    /// <summary>
    /// Interface for objects that can determine when a mesh should be split when tiling
    /// </summary>
    public interface TileSplitCriteria
    {
        /// <summary>
        /// </summary>
        /// <param name="meshOps">source mesh operators to consider splitting</param>
        /// <param name="bounds">Bounding area to consider splitting</param>
        /// <returns>non-null reason iff bounds should be subdivided</returns>
        string ShouldSplit(BoundingBox bounds, params MeshOperator[] meshOps);
    }
}
