using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using OPS.Geometry;

namespace OPS.Pipeline
{
    public class VertexSplitCriteria : ITileSplitCriteria
    {
        int targetVertexNumPerTile;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="targetFacesPerTile">Faces allowed in an a bouding region before we should split</param>
        public VertexSplitCriteria(int targetVertexNumPerTile)
        {
            this.targetVertexNumPerTile = targetVertexNumPerTile;
        }

        /// <summary>
        /// Should we split this area 
        /// </summary>
        /// <param name="meshOperator">Operator of mesh to consider splitting</param>
        /// <param name="bounds">Area to consider splitting</param>
        /// <returns>True if we should split the area</returns>
        public bool ShouldSplit(MeshOperator meshOperator, BoundingBox bounds)
        {
            int curVertexCount = meshOperator.CountVertices(bounds);
            if (curVertexCount <= this.targetVertexNumPerTile)
            {
                return false;
            }
            return true;
        }
    }
    
}
