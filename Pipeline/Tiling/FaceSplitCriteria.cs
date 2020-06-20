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
        public readonly int targetFacesPerTile;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="targetFacesPerTile">Faces allowed in a bouding region before we should split</param>
        public FaceSplitCriteria(int targetFacesPerTile)
        {
            this.targetFacesPerTile = targetFacesPerTile;
        }

        /// <summary>
        /// Should we split this area 
        /// </summary>
        /// <param name="meshOperator">Operator of mesh to consider splitting</param>
        /// <param name="bounds">Area to consider splitting</param>
        /// <returns>True if we should split the area</returns>
        public bool ShouldSplit(MeshOperator meshOperator, BoundingBox bounds)
        {
            int curPolyCount = meshOperator.CountFaces(bounds);
            if (curPolyCount <= this.targetFacesPerTile)
            {
                return false;
            }
            return true;
        }
    }
}
