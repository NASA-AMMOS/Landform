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
    /// Interface to define a tiling scheme for a mesh.  Can be used to implement quad, oct, KD trees.
    /// </summary>
    public interface ITilingScheme
    {
        /// <summary>
        /// Given a bounding area, subdivide it into a set of bounding areas of its children
        /// Filters out empty bounding areas
        /// </summary>
        /// <param name="meshOperator">Operator to use in order to filter empty tiles</param>
        /// <param name="box">Bounding area to split</param>
        /// <returns></returns>
        IEnumerable<BoundingBox> Split(MeshOperator meshOperator, BoundingBox box);

        /// <summary>
        /// Expand currentBounds to get as close as possible to desiredBounds given constraints of the tiling
        /// algorithm.  For example, a quadtree might allow expansion in one direction but not the other two.
        /// If desiredBounds is null, try to expand all dimensions to a large value.
        /// </summary>
        /// <param name="currentBounds"></param>
        /// <param name="desiredBounds"></param>
        /// <returns></returns>
        BoundingBox ExpandBounds(BoundingBox currentBounds, BoundingBox? desiredBounds);
    }
}
