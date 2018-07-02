using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sharp3DBinPacking;
using OPS.Geometry;
using OPS.Imaging;
using log4net;
using System.Windows;

namespace OPS.Pipeline
{
    public class TexturedMeshClipper
    {

        static ILog logger = LogManager.GetLogger(typeof(TexturedMeshClipper));


        public class TexturePatch
        {
            public HashSet<Triangle> triangles;
            public Image patchImage;
            BoundingBox uvBounds;

            public TexturePatch()
            {
                this.triangles = new HashSet<Triangle>();
            }

            public void Add(Triangle t, Image img, int borderSize)
            {
                var uvBounds = t.UVBounds();
                uvBounds = img.UVToPixel(uvBounds);
                uvBounds.Min.X -= borderSize;
                if(uvBounds.Min.X < 0)
                {
                    uvBounds.Min.X = 0;
                }
                uvBounds.Min.Y -= borderSize;
                if (uvBounds.Min.Y < 0)
                {
                    uvBounds.Min.Y = 0;
                }
                uvBounds.Max.X += borderSize;
                if (uvBounds.Max.X >= img.Width)
                {
                    uvBounds.Max.X = img.Width - 1;
                }
                uvBounds.Max.Y += borderSize;
                if (uvBounds.Max.Y >= img.Height)
                {
                    uvBounds.Max.Y = img.Height - 1;
                }
                uvBounds = img.PixelToUv(uvBounds);
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

            public Vector2 MinPixel(Image img)
            {
                var b = img.UVToPixel(uvBounds);
                return new Vector2((int)b.Min.X, (int)b.Min.Y);
            }

            public Vector2 MaxPixel(Image img)
            {
                var b = img.UVToPixel(uvBounds);
                return new Vector2((int)b.Max.X, (int)b.Max.Y);
            }

            public void ClipImage(Image img)
            {
                var min = MinPixel(img);
                var max = MaxPixel(img);
                this.patchImage = new Image(img.Bands, (int)max.X - (int)min.X, (int)max.Y - (int)min.Y);
                for (int b = 0; b < patchImage.Bands; b++)
                {
                    for (int r = 0; r < patchImage.Height; r++)
                    {
                        for (int c = 0; c < patchImage.Width; c++)
                        {
                            patchImage[b, r, c] = img[b, r + (int)min.Y, c + (int)min.X];
                        }
                    }
                }
            }
        }

        List<TexturePatch> ComputePatches(Mesh mesh, Image img, int borderSize)
        {
            MeshOperator op = new MeshOperator(mesh);
            var triangles = op.Triangles;
            List<TexturePatch> patches = new List<TexturePatch>();

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
                    TexturePatch patch = new TexturePatch();
                    Queue<Triangle> trianglesToProcess = new Queue<Triangle>();
                    trianglesToProcess.Enqueue(triangles[i]);
                    while (trianglesToProcess.Count > 0)
                    {
                        var t = trianglesToProcess.Dequeue();
                        if (patch.Contains(t))
                        {
                            continue;
                        }
                        patch.Add(t, img, borderSize);
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
        /// Given a mesh and image, returns a mesh clipped to the clipping bounds and an image containing texture data for that portion of the mesh
        /// The returned image may be repacked to fit in a smaller texture
        /// </summary>
        /// <param name="inputPair"></param>
        /// <param name="clipBounds"></param>
        /// <returns></returns>
        public MeshImagePair ClipMesh(MeshImagePair inputPair, BoundingBox clipBounds, int borderSize = 5, bool allowRotation = false)
        {
            if (allowRotation == true)
            {
                logger.Warn("Clip Mesh rotation is potentially unstable and may result in half pixel texture offsets");
            }

            Image img = inputPair.Image;
            MeshOperator meshOperator = new MeshOperator(inputPair.Mesh);
            var clippedMesh = meshOperator.Clip(clipBounds);
            clippedMesh.Clean();
            List<TexturePatch> patches = ComputePatches(clippedMesh, img, borderSize);


            int clippedArea = 0;
            int maxWidth = 0, maxHeight = 0;
            for (int b = 0; b < patches.Count; b++)
            {
                patches[b].ClipImage(img);
                clippedArea += patches[b].patchImage.Width * patches[b].patchImage.Height;
                maxWidth = Math.Max(maxWidth, patches[b].patchImage.Width);
                maxHeight = Math.Max(maxHeight, patches[b].patchImage.Height);
            }

            var binWidth = MathExtensions.MathE.CeilPowerOf2(maxWidth);
            var binHeight = MathExtensions.MathE.CeilPowerOf2(maxHeight);
            var binDepth = 1;

            Cuboid[] cuboids = new Cuboid[patches.Count];
            for (int i = 0; i < patches.Count; i++)
            {
                cuboids[i] = new Cuboid(patches[i].patchImage.Width, patches[i].patchImage.Height, 1, 0, patches[i]);
            }
            BinPackResult packed = null;
            var numBins = 0;
            while (numBins != 1)
            {
                var parameter = new BinPackParameter(binWidth, binHeight, binDepth, 0, allowRotation, cuboids);

                // Create a bin packer instance
                // The default bin packer will test all algorithms and try to find the best result
                // BinPackerVerifyOption is used to avoid bugs, it will check whether the result is correct
                var binPacker = BinPacker.GetDefault(BinPackerVerifyOption.BestOnly);
                packed = binPacker.Pack(parameter);
                numBins = packed.BestResult.Count;
                if (numBins != 1)
                {
                    if (binWidth <= binHeight)
                    {
                        binWidth *= 2;
                    }
                    else
                    {
                        binHeight *= 2;
                    }
                }
            }

            Image packedImg = new Image(img.Bands, binWidth, binHeight);
            var cubes = packed.BestResult.First();
            foreach (var cube in cubes)
            {
                var patch = (TexturePatch)cube.Tag;
                bool rotate = false;
                if (patch.patchImage.Width != cube.Width || patch.patchImage.Height != cube.Height)
                {
                    rotate = true;
                    patch.patchImage = patch.patchImage.Rotate90();
                }
                for (int b = 0; b < patch.patchImage.Bands; b++)
                {
                    for (int r = 0; r < patch.patchImage.Height; r++)
                    {
                        for (int c = 0; c < patch.patchImage.Width; c++)
                        {
                            packedImg[b, r + (int)cube.Y, c + (int)cube.X] = patch.patchImage[b, r, c];
                        }
                    }
                }
                foreach (var t in patch.triangles)
                {
                    foreach (var v in t.Vertices())
                    {
                        Vector2 orignPixel = img.UVToPixel(v.UV);
                        Vector2 patchPixel = orignPixel - patch.MinPixel(img);
                        Vector2 destPixel = patchPixel;
                        if (rotate)
                        {
                            destPixel = new Vector2((int)(patch.patchImage.Width - patchPixel.Y - 1), (int)patchPixel.X);
                        }
                        destPixel += new Vector2((int)cube.X, (int)cube.Y);
                        Vector2 destUV = packedImg.PixelToUV(destPixel);
                        v.UV = destUV;
                    }
                }

            }
            List<Triangle> resultTriangles = new List<Triangle>();
            foreach (var p in patches)
            {
                resultTriangles.AddRange(p.triangles);
            }

            var result = new MeshImagePair();
            result.Image = packedImg;
            result.Mesh = new Mesh(resultTriangles, hasNormals: inputPair.Mesh.HasNormals, hasUVs: inputPair.Mesh.HasUVs, hasColors: inputPair.Mesh.HasColors);
            return result;
        }

    }
}
