using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using OPS.Util;
using OPS.Imaging;
using OPS.Geometry;

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
        private class Dataset
        {
            public Mesh Mesh;
            public Image Image;
            public MeshOperator MeshOperator;
            public Dataset(Mesh mesh, Image img)
            {
                if (!mesh.HasNormals)
                {
                    mesh.GenerateVertexNormals();
                }
                this.Mesh = mesh;
                this.Image = img;
                MeshOperator = new MeshOperator(mesh, buildFaceTree: true, buildUVFaceTree: false,
                                                buildVertexTree: !mesh.HasFaces);
            }
        }

        public BoundingBox TotalBounds;

        private List<Dataset> inputs = new List<Dataset>();
        private TextureBaker textureBaker;
        private TexturedMeshClipper texturedMeshClipper;

        public MultiMeshClipper(int borderSize = 5, bool powerOfTwoTextures = false, bool allowRotation = false,
                                ILogger logger = null)
        {
            texturedMeshClipper = new TexturedMeshClipper(borderSize, powerOfTwoTextures, allowRotation, logger);
        }

        /// <summary>
        /// Adds a new input dataset
        /// All datasets should be added before clipping is performed
        /// Inputs should all be similar, meshes should have matching attributes
        /// and they should either all have textures or none should have textures.
        /// Otherwise the clipping behaviour is undefined
        /// </summary>
        /// <param name="dataset"></param>
        public void AddInput(Mesh mesh, Image img)
        {
            if (textureBaker != null)
            {
                throw new Exception("Cannot add dataset after calling InitTextureBaker()");
            }

            var dataset = new Dataset(mesh, img);
            inputs.Add(dataset);

            var bounds = dataset.MeshOperator.Bounds;
            TotalBounds = inputs.Count == 1 ? bounds : BoundingBoxExtensions.Union(TotalBounds, bounds);

            if (img != null && dataset.MeshOperator.HasUVs)
            {
                texturedMeshClipper.AddMeshImagePair(dataset.MeshOperator, img);
            }
        }

        /// <summary>
        /// Initiliaze the texture baker
        /// This method shold be called after all inputs have been added but before any calls to BakeTexture are made
        /// </summary>
        public void InitTextureBaker()
        {            
            var datasets = inputs
                .Where(d => d.Image != null && d.Mesh.HasUVs)
                .Select(d => new MeshImagePair(d.Mesh, d.Image))
                .ToArray();
            if (datasets.Length > 0)
            {
                textureBaker = new TextureBaker(datasets);
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
                foreach (var dataset in inputs)
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
            foreach (var dataset in inputs)
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
        /// If ragged is true the original polygons will be left intact and not clipped to straight line boundaries
        /// and any triangle that intersects with the box will be included.
        /// </summary>
        /// <param name="box"></param>
        /// <param name="ragged"></param>
        /// <returns></returns>
        public Mesh Clip(BoundingBox box, bool ragged = false)
        {
            var meshes = inputs.Where(d => !d.MeshOperator.Empty(box)).Select(d => d.MeshOperator.Clip(box, ragged));
            var merged = Mesh.Merge(meshes.ToArray());
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
        public MeshImagePair ClipWithTexture(BoundingBox box, int maxTextureSize)
        {
            return texturedMeshClipper.Clip(box, maxTextureSize);
        }

        /// <summary>
        /// Clips a merged mesh from the collection of input datasets
        /// Generates a texture for the mesh by uv-ing the mesh (if necessary) and baking
        /// color data across.  The size of the texture will match the provided
        /// size.  Depending on input resolution and output size this may over or 
        /// undersample the original data.
        /// If the provided mesh already has UVs they will be used, otherwise UVAtlas will be called to generate UVs.
        /// Caller must ensure that existing UVs utilize the full [0,1]x[0,1] UV space.
        /// If the mesh UVs came from MultiMeshClipper.Clip() then that will likely not be true.
        /// Mesh.RescaleUVs() can be used to remap the mesh UVs to [0,1]x[0,1].
        /// </summary>
        /// <param name="mesh"></param>
        /// <param name="textureSize"></param>
        /// <returns></returns>
        public MeshImagePair BakeTexture(Mesh mesh, int textureSize, double maxStretch = 1, Action<string> info = null)
        {
            if (textureBaker == null)
            {
                throw new Exception("InitTextureBaker() must be called before BakeTexture");
            }

            info = info ?? (msg => {});
            info(string.Format("atlasing mesh with UVAtlas, texture resolution {0}", textureSize));

            if (!mesh.HasUVs)
            {
                mesh = UVAtlas.Atlas(mesh, textureSize, textureSize, maxStretch: maxStretch,
                                     logger: new ThunkLogger() { Info = info });
                if(mesh == null)
                {
                    info("failed to atlas mesh for texture bake");
                    return null;
                }
            }

            info("baking texture");
            var img = textureBaker.Bake(mesh, textureSize, textureSize);

            return new MeshImagePair(mesh, img);
        }
    }
}

