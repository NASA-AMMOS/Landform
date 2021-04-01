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
    public class FaceSplitCriteria : TileSplitCriteria
    {
        public readonly int maxFaces; //unlimited if non-positive

        public FaceSplitCriteria(int maxFaces)
        {
            this.maxFaces = maxFaces;
        }

        public string ShouldSplit(BoundingBox bounds, params MeshOperator[] meshOps)
        {
            if (maxFaces <= 0)
            {
                return null;
            }
            int faces = meshOps.Sum(meshOp => meshOp.CountFaces(bounds));
            return faces > maxFaces ? $"{faces} > {maxFaces} faces" : null;
        }
    }
}
