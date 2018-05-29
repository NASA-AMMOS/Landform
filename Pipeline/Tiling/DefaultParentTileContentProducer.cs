using OPS.Geometry;
using OPS.Imaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Pipeline
{
    /// <summary>
    /// Generic class for generating parent tiles with a specified face limit
    /// </summary>
    public class DefaultParentTileContentProducer : IContentProducer
    {
        int parentTileResamplePoints;
        int parentTileFaceLimit;
        ITilingScheme tilingScheme;
        IMeshAtlaser atlaser;
        IImageProducer imageProducer;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="parentTileFaceLimit">max faces allowed for parent tiles</param>
        /// <param name="tilingScheme">tilingScheme used to calculate new bounds of tiles (quadtree tiles for example clip in horizontal dimesnions but not vertical)</param>
        /// <param name="atlaser">If supplied will be used to atlas parent meshes</param>
        /// <param name="imageProducer">If supplied will be used to generate images for parent meshes, must also supply atlaser</param>
        /// <param name="parentTileResamplePoints">Number of samples to use when generating parent mesh</param>
        public DefaultParentTileContentProducer(int parentTileFaceLimit, ITilingScheme tilingScheme, IMeshAtlaser atlaser = null, IImageProducer imageProducer = null, int parentTileResamplePoints = 6000)
        {
            this.parentTileFaceLimit = parentTileFaceLimit;
            this.parentTileResamplePoints = parentTileResamplePoints;
            this.tilingScheme = tilingScheme;
            this.atlaser = atlaser;
            this.imageProducer = imageProducer;
        }

        /// <summary>
        /// Generate mesh and image content for the node an attach it
        /// </summary>
        /// <param name="curNode"></param>
        /// <param name="meshOperator"></param>
        public void ProduceContent(SceneNode curNode, MeshOperator meshOperator)
        {            
            // Get children
            Mesh[] childMeshes = curNode.Children.Select(child => (child.GetComponent<MeshImagePair>()).Mesh).Where(m => m != null).ToArray();
            // Merge into a parent mesh
            Mesh parentMesh = Mesh.Merge(true, false, false, childMeshes);
            // Resample, Remesh, and decimate
            Mesh decimatedMesh = FSSR.ResampleDeimation(parentMesh, parentTileResamplePoints, parentTileFaceLimit);
            // Recompute bounds
            curNode.GetOrAddComponent<NodeBounds>().Bounds = tilingScheme.ExpandBounds(curNode.GetOrAddComponent<NodeBounds>().Bounds, decimatedMesh.Bounds());
            // Clip the mesh to the new bounds
            Mesh clippedMesh = Mesh.Clip(decimatedMesh, curNode.GetOrAddComponent<NodeBounds>().Bounds);
            // Atlaas and generate image
            if (atlaser != null)
            {
                clippedMesh = atlaser.GenerateAtlas(clippedMesh);
            }
            Image img = null;
            if (imageProducer != null)
            {
                img = imageProducer.GenerateImage(clippedMesh);
            }
            // Add content to node
            MeshImagePair pair = curNode.AddComponent<MeshImagePair>();
            pair.Mesh = clippedMesh;
            pair.Image = img;
        }
    }
}
