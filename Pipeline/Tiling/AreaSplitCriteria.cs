using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using OPS.Geometry;

namespace OPS.Pipeline
{
    /// <summary>
    /// Splitting criteria to split tiles based on a max mesh area
    /// </summary>
    public class AreaSplitCriteria : TileSplitCriteria
    {
        public readonly double maxArea; //unlimited if non-positive

        public AreaSplitCriteria(double maxArea)
        {
            this.maxArea = maxArea;
        }

        public bool ShouldSplit(BoundingBox bounds, params MeshOperator[] meshOps)
        {
            return maxArea > 0 && meshOps.Sum(meshOp => meshOp.ClippedMeshArea(bounds)) > maxArea;
        }
    }
}
