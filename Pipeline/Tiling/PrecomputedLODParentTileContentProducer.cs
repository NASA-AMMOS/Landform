using OPS.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Pipeline
{
    /// <summary>
    /// Produces parent tiles using pre-computed mesh LODS.  This method can be useful in cases where a better
    /// decimation can be occured by decimating the input mesh globally rather than on a tile by tile basis.
    /// </summary>
    public class PrecomputedLODParentTileContentProducer : IContentProducer
    { 
        List<MeshOperator> lods; // sorted in order of highest to lowest detail
        int faceLimit;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="lods">list of LODs in order of highest to lowest detail</param>
        /// <param name="parentTileFaceLimit"></param>
        public PrecomputedLODParentTileContentProducer(List<MeshOperator> lods, int parentTileFaceLimit)
        {
            this.lods = lods;
            this.faceLimit = parentTileFaceLimit;
        }

        /// <summary>
        /// Produce mesh content for this tile and add it to the current node
        /// Does not yet support producing image content
        /// </summary>
        /// <param name="curNode"></param>
        /// <param name="meshOperator"></param>
        public void ProduceContent(SceneNode curNode, MeshOperator meshOperator)
        {
            //  try them in order of least detail to most detail
            Mesh m = null;
            for (int i = lods.Count - 1; i >= 0; i--)
            {
                MeshOperator curOperator = lods[i];
                if (curOperator.CountFaces(curNode.Bounds) <= this.faceLimit)
                {
                    m = curOperator.Clip(curNode.Bounds);
                    break;
                }
            }
            if (m != null && m.Vertices.Count > 0)
            {
                curNode.Content.Add(new MeshImagePair(m, null));
            }
        }
    }
}
