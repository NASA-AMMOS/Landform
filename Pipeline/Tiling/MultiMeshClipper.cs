using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using JPLOPS.Util;
using JPLOPS.Imaging;
using JPLOPS.Geometry;

namespace JPLOPS.Pipeline
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
        public BoundingBox TotalBounds { get; private set; }

        private List<MeshImagePair> inputs = new List<MeshImagePair>();
        private TextureBaker textureBaker;
        private TexturedMeshClipper texturedMeshClipper;

        public MultiMeshClipper(int borderSize = TilingDefaults.TEXTURE_PATCH_BORDER_SIZE,
                                bool powerOfTwoTextures = TilingDefaults.POWER_OF_TWO_TEXTURES,
                                bool allowRotation = TilingDefaults.TEXTURE_PATCH_ALLOW_ROTATION,
                                ILogger logger = null)
        {
            texturedMeshClipper = new TexturedMeshClipper(borderSize, powerOfTwoTextures, allowRotation, logger);
        }

        /// <summary>
        /// Adds a new input dataset
        /// All inputs should be added before clipping is performed
        /// Inputs should all be similar, meshes should have matching attributes
        /// and they should either all have textures or none should have textures.
        /// Otherwise the clipping behaviour is undefined
        /// </summary>
        public void AddInput(MeshImagePair mip)
        {
            if (textureBaker != null)
            {
                throw new Exception("cannot add input after calling InitTextureBaker()");
            }

            mip.EnsureMeshOperator();

            inputs.Add(mip);

            var bounds = mip.MeshOp.Bounds;
            TotalBounds = inputs.Count == 1 ? bounds : BoundingBoxExtensions.Union(TotalBounds, bounds);

            if (mip.Image != null && mip.Mesh.HasUVs)
            {
                texturedMeshClipper.AddInput(mip);
            }
        }

        /// <summary>
        /// Initialize the texture baker
        /// This method shold be called after all inputs have been added but before any calls to BakeTexture are made
        /// </summary>
        public void InitTextureBaker()
        {            
            var pairs = inputs.Where(d => d.Image != null && d.Mesh.HasUVs).ToArray();
            if (pairs.Length > 0)
            {
                textureBaker = new TextureBaker(pairs);
            }
        }

        public MeshOperator[] GetMeshOps()
        {
            return inputs.Select(mip => mip.MeshOp).ToArray();
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
            var meshes = inputs.Where(mip => !mip.MeshOp.Empty(box)).Select(mip => mip.MeshOp.Clipped(box, ragged));
            var merged = MeshMerge.Merge(meshes.ToArray());
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
        public MeshImagePair ClipWithTexture(BoundingBox box, int maxTextureSize = -1, double maxTexelsPerMeter = -1)
        {
            return texturedMeshClipper.Clip(box, maxTextureSize, maxTexelsPerMeter);
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
                if (!UVAtlas.Atlas(mesh, textureSize, textureSize, maxStretch: maxStretch,
                                   logger: new ThunkLogger() { Info = info }))
                {
                    info("failed to atlas mesh with UVAtlas");
                    return null;
                }
            }

            info("baking texture");
            var img = textureBaker.Bake(mesh, textureSize, textureSize, out Image index);

            return new MeshImagePair(mesh, img, index);
        }
    }
}

