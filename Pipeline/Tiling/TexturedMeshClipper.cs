using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Sharp3DBinPacking;
using JPLOPS.MathExtensions;
using JPLOPS.Util;
using JPLOPS.Geometry;
using JPLOPS.Imaging;

namespace JPLOPS.Pipeline
{
    public class TexturedMeshClipper
    {
        public const double NON_POWER_OF_TWO_BIN_GROW_FACTOR = 1.1;

        private List<MeshImagePair> inputs = new List<MeshImagePair>();

        private int borderSize;
        private bool powerOfTwoTextures;
        private bool allowRotation;
        private ILogger logger;
        private string logPrefix;
        
        /// <summary>
        /// If rotation is allowed in packing, small pixel texture may be introduced.
        /// rotation is potentially unstable and may result in half pixel texture offsets
        /// </summary>
        public TexturedMeshClipper(int borderSize = TilingDefaults.TEXTURE_PATCH_BORDER_SIZE,
                                   bool powerOfTwoTextures = TilingDefaults.POWER_OF_TWO_TEXTURES,
                                   bool allowRotation = TilingDefaults.TEXTURE_PATCH_ALLOW_ROTATION,
                                   ILogger logger = null, string logPrefix = null)
        {
            this.borderSize = borderSize;
            this.powerOfTwoTextures = powerOfTwoTextures;
            this.allowRotation = allowRotation;
            this.logger = logger;
            this.logPrefix = logPrefix ?? "textured mesh clipper";
        }

        /// <summary>
        /// add an input dataset
        /// </summary>
        public void AddInput(MeshImagePair mip)
        {
            if (!mip.Mesh.HasUVs)
            {
                throw new Exception("expecting uvs on textured mesh clip");
            }
            mip.EnsureMeshOperator();
            inputs.Add(mip);
        }

        /// <summary>
        /// Clips every mesh to specified bounding box.
        /// Creates new combined texture of packed patches from original images for each portion of clipped mesh.
        /// Creates new combined mesh merging all the clipped triangles.
        /// If maxTextureSize > 0 then the combined texture is resized if necessary before return.
        /// </summary>
        public MeshImagePair Clip(BoundingBox box, int maxTextureSize = -1, double maxTexelsPerMeter = -1)
        {
            var patches = new List<TexturePatch>();
            double area = 0;
            foreach (var mip in inputs)
            {
                Mesh mesh = mip.MeshOp.Clipped(box);
                if (maxTextureSize > 0 && maxTexelsPerMeter > 0)
                {
                    area += mesh.SurfaceArea();
                }
                patches.AddRange(ComputePatches(mesh, mip.Image, mip.Index));
            }
            if (area > 0)
            {
                maxTextureSize = SceneNodeTilingExtensions.
                    GetTileResolution(area, maxTextureSize, -1, maxTexelsPerMeter, powerOfTwoTextures);
            }
            return ClipAndRemapPatches(patches, maxTextureSize);
        }

        /// <summary>
        /// Clip out the portion of fullImage used by clippedMesh, then remap the UVs of clippedMesh to match.
        /// Note: mutates the UVs of clippedMesh in place.
        /// If maxTextureSize > 0 then the combined texture is resized if necessary before return.
        /// </summary>
        public MeshImagePair RemapMeshClipImage(Mesh clippedMesh, Image fullImage, Image fullImageIndex = null,
                                                int maxTextureSize = -1, double maxTexelsPerMeter = -1)
        {
            if (!clippedMesh.HasUVs)
            {
                throw new ArgumentException("clipped mesh must have UVs");
            }
            if (maxTextureSize < 0 && maxTexelsPerMeter > 0)
            {
                maxTextureSize = SceneNodeTilingExtensions.
                    GetTileResolution(clippedMesh, maxTextureSize, -1, maxTexelsPerMeter, powerOfTwoTextures);
            }
            var patches = ComputePatches(clippedMesh, fullImage, fullImageIndex);
            return ClipAndRemapPatches(patches, maxTextureSize, clippedMesh);
        }

        /// <summary>
        /// Portion of an original texture that is being used, along with triangles textured by it.
        /// </summary>
        private class TexturePatch
        {
            public readonly HashSet<Triangle> triangles = new HashSet<Triangle>();
            public readonly Image originalImage, originalIndex;
            public readonly bool hasNormals, hasColors;

            public Image patchImage, patchIndex;

            private BoundingBox uvBounds;

            public TexturePatch(Image originalImage, Image originalIndex, bool hasNormals, bool hasColors)
            {
                this.originalImage = originalImage;
                this.originalIndex = originalIndex;
                this.hasNormals = hasNormals;
                this.hasColors = hasColors;
            }

            /// <summary>
            /// Merge UV BoundingBox of given triangle with border included with the bounding box of patch 
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
            /// crops image corresponding to the patch from the original image
            /// </summary>
            public void ClipImage()
            {
                Vector2 min = MinPixel(), max = MaxPixel();

                patchImage = originalImage.Crop(startRow: (int)min.Y, startCol: (int)min.X,
                                                newWidth: (int)(max.X - min.X + 1),
                                                newHeight: (int)(max.Y - min.Y + 1));
                if (originalIndex != null)
                {
                    patchIndex = originalIndex.Crop(startRow: (int)min.Y, startCol: (int)min.X,
                                                    newWidth: (int)(max.X - min.X + 1),
                                                    newHeight: (int)(max.Y - min.Y + 1));
                }
            }
        }

        /// <summary>
        /// Creates a TexturePatch for every group of triangles whose UVBounds intersect.
        /// </summary>
        private List<TexturePatch> ComputePatches(Mesh mesh, Image img, Image index)
        {
            mesh.Clean();
            var op = new MeshOperator(mesh, buildFaceTree: false, buildUVFaceTree: true, buildVertexTree: false);
            var patches = new List<TexturePatch>();
            for (int i = 0; i < op.FaceCount; i++)
            {
                Triangle triangle = op.GetTriangle(i);
                bool skip = false;
                foreach (var patch in patches)
                {
                    if (patch.Contains(triangle))
                    {
                        skip = true;
                        break;
                    }
                }
                if (!skip)
                {
                    var patch = new TexturePatch(img, index, mesh.HasNormals, mesh.HasColors);
                    var trianglesToProcess = new Queue<Triangle>();
                    trianglesToProcess.Enqueue(triangle);
                    while (trianglesToProcess.Count > 0)
                    {
                        var t = trianglesToProcess.Dequeue();
                        if (patch.Contains(t))
                        {
                            continue;
                        }
                        patch.Add(t, borderSize);
                        foreach (var inter in op.UVIntersects(t.UVBounds()))
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
        private MeshImagePair ClipAndRemapPatches(List<TexturePatch> patches, int maxTextureSize, Mesh mesh = null)
        {
            Action<string> warn = msg => { if (logger != null) logger.LogWarn(logPrefix + " " + msg); };

            int packedWidth = 0, packedHeight = 0;
            int maxPackedWidth = 0, maxPackedHeight = 0;
            int minPackedArea = 0;
            int imageBands = 0;
            bool hasNormals = false, hasColors = false;
            bool oneOriginalImage = true, oneOriginalIndex = true;
            bool generateIndex = true;
            foreach (var patch in patches)
            {
                imageBands = Math.Max(imageBands, patch.originalImage.Bands);
                hasNormals |= patch.hasNormals;
                hasColors |= patch.hasColors;
                patch.ClipImage();
                packedWidth = Math.Max(packedWidth, patch.patchImage.Width);
                packedHeight = Math.Max(packedHeight, patch.patchImage.Height);
                maxPackedWidth += patch.patchImage.Width;
                maxPackedHeight += patch.patchImage.Height;
                oneOriginalImage &= patches[0].originalImage == patch.originalImage;
                oneOriginalIndex &= patches[0].originalIndex == patch.originalIndex;
                generateIndex &= patch.originalIndex != null;
                minPackedArea += patch.patchImage.Width * patch.patchImage.Height;
            }

            if (oneOriginalImage && !oneOriginalIndex)
            {
                warn("one original image but more than one original index");
            }

            if (mesh == null)
            {
                var tris = new List<Triangle>();
                foreach (var p in patches)
                {
                    tris.AddRange(p.triangles);
                }
                mesh = new Mesh(tris, hasNormals: hasNormals, hasUVs: true, hasColors: hasColors);
            }

            if (powerOfTwoTextures)
            {
                packedWidth = MathE.CeilPowerOf2(packedWidth);
                packedHeight = MathE.CeilPowerOf2(packedHeight);
                maxPackedWidth = 2 * MathE.CeilPowerOf2(maxPackedWidth);
                maxPackedHeight = 2 * MathE.CeilPowerOf2(maxPackedHeight);
            }
            else
            {
                double f = NON_POWER_OF_TWO_BIN_GROW_FACTOR;
                f = f * f;
                maxPackedWidth = (int)Math.Ceiling(maxPackedWidth * f);
                maxPackedHeight = (int)Math.Ceiling(maxPackedHeight * f);
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
            bool packSucceded = false;
            while (packedWidth <= maxPackedWidth && packedHeight <= maxPackedHeight)
            {
                var parameter = new BinPackParameter(packedWidth, packedHeight, 1, 0, allowRotation, cuboids);
                var binPacker = BinPacker.GetDefault(BinPackerVerifyOption.BestOnly);
                packed = binPacker.Pack(parameter);
                if (packed != null && packed.BestResult != null && packed.BestResult.Count == 1)
                {
                    if (!powerOfTwoTextures)
                    {
                        int minX = int.MaxValue, minY = int.MaxValue, maxX = 0, maxY = 0;
                        foreach (var cube in packed.BestResult.First())
                        {
                            var patchULCPixelInDest = new Vector2((int)cube.X, (int)cube.Y);
                            minX = (int)Math.Min(minX, patchULCPixelInDest.X);
                            minY = (int)Math.Min(minY, patchULCPixelInDest.Y);
                            var patchLRCPixelInDest = patchULCPixelInDest +
                                new Vector2((int)(cube.Width - 1), (int)(cube.Height - 1));
                            maxX = (int)Math.Max(maxX, patchLRCPixelInDest.X);
                            maxY = (int)Math.Max(maxY, patchLRCPixelInDest.Y);
                        }
                        if (minX != 0 || minY != 0)
                        {
                            warn($"pack result minRow={minY}, minCol={minX}, not (0,0)");
                        }
                        packedWidth = maxX + 1;
                        packedHeight = maxY + 1;
                    }
                    packSucceded = true;
                    break;
                }
                grow();
            }

            if (!packSucceded)
            {
                 warn($"failed to pack {patches.Count} patches, toal {minPackedArea} pixels, " +
                      $"into at most {maxPackedWidth}x{maxPackedHeight} image");
            }

            Image img = null, index = null;
            if (oneOriginalImage && oneOriginalIndex)
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
                Image origIndex = patches[0].originalIndex;

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

                if (!packSucceded || w * h <= packedWidth * packedHeight)
                {
                    if (w < origImg.Width || h < origImg.Height)
                    {
                        if (logger != null)
                        {
                            logger.LogVerbose("{0} using {1}x{2} subregion of original image", logPrefix, w, h);
                        }

                        img = origImg.Crop((int)minPixel.Y, (int)minPixel.X, w, h);

                        if (origIndex != null)
                        {
                            index = origIndex.Crop((int)minPixel.Y, (int)minPixel.X, w, h);
                        }

                        //remap UVs
                        foreach (var v in mesh.Vertices)
                        {
                            Vector2 origUV = v.UV;
                            Vector2 origPixel = origImg.UVToPixel(origUV);
                            Vector2 destPixel = origPixel - minPixel;
                            v.UV = img.PixelToUV(destPixel);
                            if (!MeshClean.CheckUV(v.UV))
                            {
                                throw new Exception($"{logPrefix} bad UV: UV {origUV} => {v.UV}, " +
                                                    $"pixel {origPixel} => {destPixel}");
                            }
                        }

                        mesh.Clean(warn: warn);
                    }
                    else //throw in the towel
                    {
                        if (logger != null)
                        {
                            logger.LogVerbose("{0} using original image", logPrefix);
                        }
                        img = origImg;
                        index = origIndex;
                    }
                }
            }

            if (packSucceded && img == null)
            {
                //create new texture image from packing results
                if (logger != null)
                {
                    logger.LogVerbose("{0} using {1}x{2} bin packed result", logPrefix, packedWidth, packedHeight);
                }

                img = new Image(imageBands, packedWidth, packedHeight);
                img.CreateMask(true);

                if (generateIndex)
                {
                    index = new Image(3, packedWidth, packedHeight);
                }

                var newTris = new List<Triangle>(patches.Sum(p => p.triangles.Count));

                bool anyRotated = false;
                var cubes = packed.BestResult.First();
                for (int i = 0; i < cubes.Count; i++)
                {
                    var cube = cubes[i];
                    var patch = (TexturePatch)cube.Tag;

                    int origPatchHeight = patch.patchImage.Height;

                    bool rotated = false;
                    if (allowRotation &&
                        (patch.patchImage.Width != cube.Width || patch.patchImage.Height != cube.Height))
                    {
                        rotated = true;
                        patch.patchImage = patch.patchImage.Rotate90Clockwise();
                    }
                    anyRotated |= rotated;

                    var patchULCPixelInDest = new Vector2((int)cube.X, (int)cube.Y);
                    if (logger != null)
                    {
                        logger.LogVerbose("{0} patch {1} ({2}x{3}), {4}rotated, ULC row={5}, col={6} in {7}x{8} dest",
                                          logPrefix, i, patch.patchImage.Width, patch.patchImage.Height,
                                          rotated ? "" : "not ", patchULCPixelInDest.Y, patchULCPixelInDest.X,
                                          packedWidth, packedHeight);
                    }

                    img.Blit(patch.patchImage, dstCol: (int)cube.X, dstRow: (int)cube.Y, unmask: true);

                    if (index != null)
                    {
                        index.Blit(patch.patchIndex, dstCol: (int)cube.X, dstRow: (int)cube.Y, unmask: true);
                    }

                    //remap UV coordinates
                    foreach (var t in patch.triangles)
                    {
                        var newTri = new Triangle(t);
                        foreach (var v in newTri.Vertices())
                        {
                            Vector2 origUV = v.UV;
                            Vector2 origPixel = patch.originalImage.UVToPixel(origUV);
                            Vector2 patchPixel = origPixel - patch.MinPixel();
                            if (rotated)
                            {
                                double row = patchPixel.Y;
                                double col = patchPixel.X;
                                double newRow = col;
                                double newCol = origPatchHeight - row - 1;
                                patchPixel.Y = newRow;
                                patchPixel.X = newCol;
                            }
                            var destPixel = patchULCPixelInDest + patchPixel;
                            v.UV = img.PixelToUV(destPixel);
                            if (!MeshClean.CheckUV(v.UV))
                            {
                                throw new Exception($"{logPrefix} bad UV in patch {i}: UV {origUV} => {v.UV}, " +
                                                    $"pixel {origPixel} => {patchPixel} => {destPixel}");
                            }
                        }
                        newTris.Add(newTri);
                    }
                }

                //we can't just directly mutate the UVs on mesh
                //because we may be splitting verts with the same position but different UVs
                mesh.SetTriangles(newTris, warn: warn); //will merge duplicate verts and clean mesh

                //we already included extra border pixels around each patch (see borderSize)
                //but inpaint gutter just in case texture sampling goes beyond that
                img.Inpaint();

                if (anyRotated)
                {
                    warn("used bin packing with rotation, may be unstable");
                }
            }

            if (img == null)
            {
                throw new Exception(logPrefix + " failed");
            }

            if (maxTextureSize > 0 && (img.Width > maxTextureSize || img.Height > maxTextureSize))
            {
                int origWidth = img.Width;
                int origHeight = img.Height;
                img = img.ResizeMax(maxTextureSize);
                if (index != null)
                {
                    index = index.ResizeMax(maxTextureSize, nearestNeighborSampling: true);
                }
                if (logger != null)
                {
                    logger.LogVerbose("{0} {1}x{2} image to {3}x{4}, max size {5}",
                                      logPrefix, origWidth, origHeight, img.Width, img.Height, maxTextureSize);
                }
            }

            return new MeshImagePair(mesh, img, index);
        }
    }
}
