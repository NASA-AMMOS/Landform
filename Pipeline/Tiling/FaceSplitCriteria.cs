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
    /// Splitting criteria to split tiles based on a max number of allowed faces
    /// </summary>
    public class FaceSplitCriteria : ITileSplitCriteria
    {
        public readonly int maxFaces; //unlimited if non-positive

        public FaceSplitCriteria(int maxFaces)
        {
            this.maxFaces = maxFaces;
        }

        public bool ShouldSplit(BoundingBox bounds, params MeshOperator[] meshOps)
        {
            return maxFaces > 0 && meshOps.Sum(meshOp => meshOp.CountFaces(bounds)) > maxFaces;
        }
    }
}
