using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Sharp3DBinPacking;
using OPS.MathExtensions;
using OPS.Util;
using OPS.Geometry;
using OPS.Imaging;

namespace OPS.Pipeline
{
    public class TexturedMeshClipper
    {
        public const double NON_POWER_OF_TWO_BIN_GROW_FACTOR = 1.1;

        private class MeshImageOperatorPair
        {
            public Image Image;
            public MeshOperator MeshOperator;

            public MeshImageOperatorPair(MeshOperator op, Image image)
            {
                this.Image = image;
                this.MeshOperator = op;
   
            }

            public MeshImageOperatorPair(Mesh m, Image image)
            {
                this.Image = image;
                this.MeshOperator = new MeshOperator(m, buildFaceTree: true, buildVertexTree: false, buildUVFaceTree: false);
            }
        }
        private List<MeshImageOperatorPair> pairs = new List<MeshImageOperatorPair>();

        private int borderSize;
        private bool powerOfTwoTextures;
        private bool allowRotation;
        private ILogger logger;
        
        /// <summary>
        /// If rotation is allowed in packing, small pixel texture may be introduced.
        /// rotation is potentially unstable and may result in half pixel texture offsets
        /// </summary>
        public TexturedMeshClipper(int borderSize = TilingDefaults.TEXTURE_PATCH_BORDER_SIZE,
                                   bool powerOfTwoTextures = TilingDefaults.POWER_OF_TWO_TEXTURES,
                                   bool allowRotation = TilingDefaults.TEXTURE_PATCH_ALLOW_ROTATION,
                                   ILogger logger = null)
        {
            this.borderSize = borderSize;
            this.powerOfTwoTextures = powerOfTwoTextures;
            this.allowRotation = allowRotation;
            this.logger = logger;
        }

        /// <summary>
        /// Adds MeshImagePair pair to list of MeshImagePairs to be clipped
        /// </summary>
        /// <param name="pair"></param>
        public void AddMeshImagePair(MeshImagePair pair)
        {
            if (!pair.Mesh.HasUVs)
            {
                throw new Exception("Expecting uvs on textured mesh clip");
            }
            pairs.Add(new MeshImageOperatorPair(pair.Mesh, pair.Image));
        }

        public void AddMeshImagePair(MeshOperator op, Image image)
        {
            if (!op.HasUVs)
            {
                throw new Exception("Expecting uvs on textured mesh clip");
            }
            pairs.Add(new MeshImageOperatorPair(op, image));
        }

        public void AddMeshImagePair(Mesh m, Image image)
        {
            if (!m.HasUVs)
            {
                throw new Exception("Expecting uvs on textured mesh clip");
            }
            pairs.Add(new MeshImageOperatorPair(m, image));
        }

        /// <summary>
        /// Clips every mesh in the list of MeshImagePairs to specified bounding box.
        /// Creates new combined texture of packed patches from original images for each portion of clipped mesh.
        /// Creates new combined mesh merging all the clipped triangles.
        /// </summary>
        public MeshImagePair Clip(BoundingBox box, int maxTextureSize = -1)
        {
            var patches = new List<TexturePatch>();
            foreach (var pair in pairs)
            {
                patches.AddRange(ComputePatches(pair.MeshOperator.Clip(box), pair.Image));
            }
            return ClipAndRemapPatches(patches, maxTextureSize, true);
        }

        /// <summary>
        /// Clip out the portion of fullImage used by clippedMesh, then remap the UVs of clippedMesh to match.
        /// Note: mutates the UVs of clippedMesh in place.
        /// </summary>
        public MeshImagePair RemapMeshClipImage(Mesh clippedMesh, Image fullImage, int maxTextureSize = -1)
        {
            if (!clippedMesh.HasUVs)
            {
                throw new ArgumentException("clipped mesh must have UVs");
            }
            var ret = ClipAndRemapPatches(ComputePatches(clippedMesh, fullImage), maxTextureSize, false);
            ret.Mesh = clippedMesh;
            return ret;
        }

        /// <summary>
        /// Portion of an original texture that is being used, along with triangles textured by it.
        /// </summary>
        private class TexturePatch
        {
            public readonly HashSet<Triangle> triangles = new HashSet<Triangle>();
            public readonly Image originalImage;
            public readonly bool hasNormals, hasColors;

            public Image patchImage;

            private BoundingBox uvBounds;

            public TexturePatch(Image originalImage, bool hasNormals, bool hasColors)
            {
                this.originalImage = originalImage;
                this.hasNormals = hasNormals;
                this.hasColors = hasColors;
            }

            /// <summary>
            /// Merge UV BoundingBox of given triang with border included with the bounding box of patch 
            /// </summary>
            public void Add(Triangle t, int borderSize)
            {
                var uvBounds = t.UVBounds();
                uvBounds = originalImage.UVToPixel(uvBounds);
                uvBounds.Min.X = Math.Max(uvBounds.Min.X - borderSize, 0);
                uvBounds.Min.Y = Math.Max(uvBounds.Min.Y - borderSize, 0);
                uvBounds.Max.X = Math.Min(uvBounds.Max.X + borderSize, originalImage.Width - 1);
                uvBounds.Max.Y = Math.Min(uvBounds.Max.Y + borderSize, originalImage.Height - 1);
                uvBounds = originalImage.PixelToUV(uvBounds);
                if (this.triangles.Count == 0)
                {
                    this.uvBounds = uvBounds;
                }
                this.uvBounds = BoundingBox.CreateMerged(this.uvBounds, uvBounds);
                this.triangles.Add(t);
            }

            public bool Contains(Triangle t)
            {
                return triangles.Contains(t);
            }

            public Vector2 MinPixel()
            {
                var b = originalImage.UVToPixel(uvBounds);
                return new Vector2((int)b.Min.X, (int)b.Min.Y);
            }

            public Vector2 MaxPixel()
            {
                var b = originalImage.UVToPixel(uvBounds);
                return new Vector2((int)b.Max.X, (int)b.Max.Y);
            }

            /// <summary>
            /// Finds image corresponding to the patch on the original image
            /// </summary>
            public void ClipImage()
            {
                var min = MinPixel();
                var max = MaxPixel();
                this.patchImage = new Image(originalImage.Bands, (int)(max.X - min.X + 1), (int)(max.Y - min.Y + 1));
                for (int b = 0; b < patchImage.Bands; b++)
                {
                    for (int r = 0; r < patchImage.Height; r++)
                    {
                        for (int c = 0; c < patchImage.Width; c++)
                        {
                            patchImage[b, r, c] = originalImage[b, r + (int)min.Y, c + (int)min.X];
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Creates a TexturePatch for every group of triangles whose UVBounds intersect.
        /// </summary>
        private List<TexturePatch> ComputePatches(Mesh mesh, Image img)
        {
            mesh.Clean();
            var op = new MeshOperator(mesh, buildFaceTree: false, buildVertexTree: false);
            var triangles = op.Triangles;
            var patches = new List<TexturePatch>();
            for (int i = 0; i < triangles.Count; i++)
            {
                bool skip = false;
                foreach (var patch in patches)
                {
                    if (patch.Contains(triangles[i]))
                    {
                        skip = true;
                        break;
                    }
                }
                if (!skip)
                {
                    var patch = new TexturePatch(img, mesh.HasNormals, mesh.HasColors);
                    var trianglesToProcess = new Queue<Triangle>();
                    trianglesToProcess.Enqueue(triangles[i]);
                    while (trianglesToProcess.Count > 0)
                    {
                        var t = trianglesToProcess.Dequeue();
                        if (patch.Contains(t))
                        {
                            continue;
                        }
                        patch.Add(t, borderSize);
                        var intersects = op.UVIntersects(t.UVBounds());
                        foreach (var inter in intersects)
                        {
                            trianglesToProcess.Enqueue(inter);
                        }
                    }
                    patches.Add(patch);
                }
            }
            return patches;
        }

        /// <summary>
        /// Clip texture patches from all original images, pack them together into a combined texture, remap patch
        /// triangle UVs to use the combined texture, and merge all patch triangles into one mesh.
        /// Note: this mutates the patch triangle UVs in place.
        /// Uses BinPacker to assemble the texture patches into the combined texture.
        /// If there is only one original image and the union of the patch bounds is smaller than the bin packer result,
        /// then just uses the union of the patch bounds.
        /// If maxTextureSize > 0 then the combined texture is resized if necessary before return.
        /// </summary>
        private MeshImagePair ClipAndRemapPatches(List<TexturePatch> patches, int maxTextureSize, bool makeMergedMesh)
        {
            int packedWidth = 0, packedHeight = 0;
            int minPackedArea = 0;
            int imageBands = 0;
            bool hasNormals = false, hasColors = false;
            bool oneOriginalImage = true;
            foreach (var patch in patches)
            {
                imageBands = Math.Max(imageBands, patch.originalImage.Bands);
                hasNormals |= patch.hasNormals;
                hasColors |= patch.hasColors;
                patch.ClipImage();
                packedWidth = Math.Max(packedWidth, patch.patchImage.Width);
                packedHeight = Math.Max(packedHeight, patch.patchImage.Height);
                oneOriginalImage &= patches[0].originalImage == patch.originalImage;
                minPackedArea += patch.patchImage.Width * patch.patchImage.Height;
            }

            if (powerOfTwoTextures)
            {
                packedWidth = MathE.CeilPowerOf2(packedWidth);
                packedHeight = MathE.CeilPowerOf2(packedHeight);
            }

            void grow()
            {
                double factor = powerOfTwoTextures ? 2.0 : NON_POWER_OF_TWO_BIN_GROW_FACTOR;
                if (packedWidth <= packedHeight)
                {
                    packedWidth = (int)(packedWidth * factor);
                }
                else
                {
                    packedHeight = (int)(packedHeight * factor);
                }
            }

            while (packedWidth * packedHeight < minPackedArea)
            {
                grow();
            }

            Cuboid[] cuboids = new Cuboid[patches.Count];
            for (int i = 0; i < patches.Count; i++)
            {
                cuboids[i] = new Cuboid(patches[i].patchImage.Width, patches[i].patchImage.Height, 1, 0, patches[i]);
            }
            BinPackResult packed = null;

            //pack patches and adjust bin width and height until all patches packed into one bin
            //should always terminate because bin size increases every iteration
            while (true)
            {
                var parameter = new BinPackParameter(packedWidth, packedHeight, 1, 0, allowRotation, cuboids);
                var binPacker = BinPacker.GetDefault(BinPackerVerifyOption.BestOnly);
                packed = binPacker.Pack(parameter);
                if (packed != null && packed.BestResult != null && packed.BestResult.Count == 1)
                {
                    break;
                }
                grow();
            }

            Image img = null;
            if (oneOriginalImage)
            {
                //check if it's more efficient to just crop out union of original patches than to use bin packing result
                Vector2 minPixel = new Vector2(double.PositiveInfinity, double.PositiveInfinity);
                Vector2 maxPixel = new Vector2(double.NegativeInfinity, double.NegativeInfinity);
                foreach (var patch in patches)
                {
                    Vector2 l = patch.MinPixel();
                    minPixel.X = Math.Min(minPixel.X, l.X);
                    minPixel.Y = Math.Min(minPixel.Y, l.Y);
                    Vector2 u = patch.MaxPixel();
                    maxPixel.X = Math.Max(maxPixel.X, u.X);
                    maxPixel.Y = Math.Max(maxPixel.Y, u.Y);
                }
                int w = (int)(maxPixel.X - minPixel.X + 1);
                int h = (int)(maxPixel.Y - minPixel.Y + 1);
                Image origImg = patches[0].originalImage;
                if (powerOfTwoTextures)
                {
                    w = MathE.CeilPowerOf2(w);
                    h = MathE.CeilPowerOf2(h);
                    if (w > origImg.Width || h > origImg.Height)
                    {
                        //origImg may not itself be power of two
                        //but let's define our contract to only to be power of two *if* we generate a new texture
                        w = origImg.Width;
                        h = origImg.Height;
                        minPixel = new Vector2(0, 0);
                    }
                    else
                    {
                        if (minPixel.X + w > origImg.Width)
                        {
                            minPixel.X = origImg.Width - w;
                        }
                        if (minPixel.Y + h > origImg.Height)
                        {
                            minPixel.Y = origImg.Height - h;
                        }
                    }
                }
                if (w * h <= packedWidth * packedHeight)
                {
                    if (w < origImg.Width || h < origImg.Height)
                    {
                        if (logger != null)
                        {
                            logger.LogVerbose("textured mesh clipper using {0}x{1} subregion of original image", w, h);
                        }
                        img = origImg.Crop((int)minPixel.Y, (int)minPixel.X, w, h);
                        foreach (var patch in patches)
                        {
                            foreach (var t in patch.triangles)
                            {
                                foreach (var v in t.Vertices())
                                {
                                    v.UV = img.PixelToUV(origImg.UVToPixel(v.UV) - minPixel);
                                }
                            }
                        }
                    }
                    else
                    {
                        if (logger != null)
                        {
                            logger.LogVerbose("textured mesh clipper using original image");
                        }
                        img = origImg;
                    }
                }
            }

            if (img == null)
            {
                //create new texture image from packing results
                if (logger != null)
                {
                    logger.LogVerbose("textured mesh clipper using {0}x{1} bin packed result",
                                      packedWidth, packedHeight);
                }
                img = new Image(imageBands, packedWidth, packedHeight);
                var cubes = packed.BestResult.First();
                bool anyRotated = false;
                foreach (var cube in cubes)
                {
                    var patch = (TexturePatch)cube.Tag;
                    bool rotated = false;
                    if (allowRotation &&
                        (patch.patchImage.Width != cube.Width || patch.patchImage.Height != cube.Height))
                    {
                        rotated = true;
                        patch.patchImage = patch.patchImage.Rotate90Clockwise();
                    }
                    for (int b = 0; b < patch.patchImage.Bands; b++)
                    {
                        for (int r = 0; r < patch.patchImage.Height; r++)
                        {
                            for (int c = 0; c < patch.patchImage.Width; c++)
                            {
                                img[b, r + (int)cube.Y, c + (int)cube.X] = patch.patchImage[b, r, c];
                            }
                        }
                    }
                    anyRotated |= rotated;
                    //remap UV coordinates
                    foreach (var t in patch.triangles)
                    {
                        foreach (var v in t.Vertices())
                        {
                            Vector2 origPixel = patch.originalImage.UVToPixel(v.UV);
                            Vector2 patchPixel = origPixel - patch.MinPixel();
                            Vector2 destPixel = patchPixel;
                            if (rotated)
                            {
                                destPixel = new Vector2((int)(patch.patchImage.Width - patchPixel.Y - 1),
                                                        (int)patchPixel.X);
                            }
                            destPixel += new Vector2((int)cube.X, (int)cube.Y);
                            v.UV = img.PixelToUV(destPixel);
                        }
                    }
                }
                if (anyRotated && logger != null)
                {
                    logger.LogWarn("textured mesh clipper used bin packing with rotation, may be unstable");
                }
            }

            if (maxTextureSize > 0 && (img.Width > maxTextureSize || img.Height > maxTextureSize))
            {
                int origWidth = img.Width;
                int origHeight = img.Height;
                img = img.ResizeMax(maxTextureSize);
                if (logger != null)
                {
                    logger.LogVerbose("textured mesh clipper downsized {0}x{1} image to {2}x{3}, max size {4}",
                                      origWidth, origHeight, img.Width, img.Height, maxTextureSize);
                }
            }

            MeshImagePair result = new MeshImagePair();
            result.Image = img;

            if (makeMergedMesh)
            {
                var resultTriangles = new List<Triangle>();
                foreach (var p in patches)
                {
                    resultTriangles.AddRange(p.triangles);
                }
                result.Mesh = new Mesh(resultTriangles, hasNormals: hasNormals, hasUVs: true, hasColors: hasColors);
            }

            return result;
        }
    }
}
