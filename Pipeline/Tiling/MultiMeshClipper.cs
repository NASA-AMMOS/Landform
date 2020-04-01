using log4net;
using Microsoft.Xna.Framework;
using OPS.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Imaging;

namespace OPS.Pipeline
{
    /// <summary>
    /// This class enables the user to load multiple input meshes (with optional textures)
    /// and perform clip operations against them as a collect.  The result of the clip will be
    /// a single mesh represeting the merged geometry of the clipped input meshes.  Depending on
    /// the method used, a single output texture can also be generated that combines input textures
    /// from all of the source image products.  Both texture baking and atlas clipping / repacking are supported.
    /// </summary>
    public class MultiMeshClipper
    {
        static ILog logger = LogManager.GetLogger(typeof(MultiMeshClipper));


        public BoundingBox TotalBounds;
        public List<MultiMeshClipperInput> Inputs;
        public TextureBaker TextureBaker;
        public TexturedMeshClipper TexturedMeshClipper;

        bool textureBakerInitialized = false;

        public MultiMeshClipper()
        {
            this.Inputs = new List<MultiMeshClipperInput>();
            TexturedMeshClipper = new TexturedMeshClipper();
        }

        /// <summary>
        /// Adds a new input dataset
        /// All datasets should be added before clipping is performed
        /// Inputs should all be similar, meshes should have matching attributes
        /// and they should either all have textures or none should have textures.
        /// Otherwise the clipping behaviour is undefined
        /// </summary>
        /// <param name="dataset"></param>
        public void AddInput(MultiMeshClipperInput dataset)
        {
            if(textureBakerInitialized)
            {
                throw new Exception("Cannot add dataset after calling InitTextureBaker()");
            }
            if (this.Inputs.Count == 0)
            {
                TotalBounds = dataset.MeshOperator.Bounds;
            }
            TotalBounds = BoundingBoxExtensions.Union(TotalBounds, dataset.MeshOperator.Bounds);
            this.Inputs.Add(dataset);
            if (dataset.Image != null)
            {
                this.TexturedMeshClipper.AddMeshImagePair(dataset.Mesh, dataset.Image);
            }
        }

        /// <summary>
        /// Initialize the texture baker
        /// This method shold be called after all inputs have been added but before any calls to BakeTexture are made
        /// </summary>
        public void InitTextureBaker(bool bakeIndexImages = false)
        {            
            if (!textureBakerInitialized)
            {
                textureBakerInitialized = true;
                var filtered = this.Inputs.Where(d => d.Image != null);
                if(bakeIndexImages)
                {
                    filtered = filtered.Where(d => d.Index != null);
                }
                var datasets = filtered.Select(d => new MeshImagePair(d.Mesh, d.Image)).ToArray();
                if (datasets.Length > 0)
                {
                    var indexImgs = bakeIndexImages ?
                                    filtered.Select(d => new IndexImage(d.Index)).ToArray() :
                                    null;
                    TextureBaker = new TextureBaker(datasets, indexImgs);
                }
            }
            else
            {
                logger.Warn("Already initialized");
            }
        }

        /// <summary>
        /// Given a list of bounding boxes, filters out any that do not contain geometry from any of the input datasets 
        /// </summary>
        /// <param name="boxes"></param>
        /// <returns></returns>
        public IEnumerable<BoundingBox> FilterEmptyBounds(IEnumerable<BoundingBox> boxes)
        {
            List<BoundingBox> results = new List<BoundingBox>();
            foreach (var b in boxes)
            {
                foreach (var dataset in Inputs)
                {
                    if (!dataset.MeshOperator.Empty(b))
                    {
                        results.Add(b);
                        break;
                    }
                }
            }
            return results;
        }

        /// <summary>
        /// Returns true if any of the input datasets meet the split criteria
        /// </summary>
        /// <param name="splitCriteria"></param>
        /// <param name="box"></param>
        /// <returns></returns>
        public bool ShouldSplit(ITileSplitCriteria splitCriteria, BoundingBox box)
        {
            foreach (var dataset in Inputs)
            {
                // Issue #221
                // This only checks if any single input needs to be split
                // We really want to check if collection of all datasets needs to be split
                if (splitCriteria.ShouldSplit(dataset.MeshOperator, box))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Clips the collection of input meshes and returns a single merged mesh as a result
        /// If ragged is set to true the original polygons will be left intact and not clipped to straight line boundaries
        /// and any triangle that intersects with the box will be included.
        /// </summary>
        /// <param name="box"></param>
        /// <param name="ragged"></param>
        /// <returns></returns>
        public Mesh Clip(BoundingBox box, bool ragged = false)
        {
            var meshes = this.Inputs.Where(d => !d.MeshOperator.Empty(box)).Select(d => d.MeshOperator.Clip(box, ragged)).ToArray();
            var merged = Mesh.Merge(meshes);
            merged.Clean();
            return merged;
        }

        /// <summary>
        /// Clips a merged mesh from the collection of input datasets
        /// Generates a single merged texture by cutting out and repacking the
        /// relevant portions of source texutre.  The returned image will be large enough
        /// to contain all the source image data.
        /// </summary>
        /// <param name="box"></param>
        /// <returns></returns>
        public MeshImagePair ClipWithTexture(BoundingBox box)
        {
            return this.TexturedMeshClipper.Clip(box);
        }

        /// <summary>
        /// Clips a merged mesh from the collection of input datasets
        /// Generates a texture for the mesh by uv-ing the mesh and baking
        /// color data across.  The size of the texture will match the provided
        /// size.  Depending on input resolution and output size this may over or 
        /// undersample the original data.
        /// </summary>
        /// <param name="mesh"></param>
        /// <param name="textureSize"></param>
        /// <returns></returns>
        public MeshImagePair BakeTexture(Mesh mesh, int textureSize, out Image destIndex, Action<string> info = null)
        {
            info = info ?? (msg => {});
            if(!textureBakerInitialized)
            {
                throw new Exception("InitTextureBaker() must be called before BakeTexture");
            }
            var box = mesh.Bounds();

            info(string.Format("atlasing mesh with UVAtlas, texture resolution {0}", textureSize));
            mesh = UVAtlas.Atlas(mesh, textureSize, textureSize);
            if(mesh == null)
            {
                info("failed to atlas mesh for texture bake");
                destIndex = null;
                return null;
            }

            info("baking texture");
            var img = TextureBaker.Bake(mesh, textureSize, textureSize, out destIndex);

            return new MeshImagePair(mesh, img);
        }
    }
}

