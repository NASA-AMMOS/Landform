using Microsoft.Xna.Framework;
using OPS.Geometry;
using OPS.Imaging;
using Sharp3DBinPacking;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using log4net;


namespace OPS.Pipeline
{
    /// <summary>
    /// Given one or more MeshImagePairs, can clip to create one textured mesh
    /// </summary>
    public class TexturedMeshClipper
    {
        static ILog logger = LogManager.GetLogger(typeof(TexturedMeshClipper));


        class MeshImageOperatorPair
        {
            public Image Image;
            public MeshOperator MeshOperator;

            public MeshImageOperatorPair( MeshOperator op, Image image)
            {
                this.Image = image;
                this.MeshOperator = op;
   
            }

            public MeshImageOperatorPair( Mesh m, Image image)
            {
                this.Image = image;
                this.MeshOperator = new MeshOperator(m, buildFaceTree: true, buildVertexTree: false, buildUVFaceTree: false);
            }
        }

        List<MeshImageOperatorPair> pairs;
        
        public TexturedMeshClipper()
        {
            pairs = new List<MeshImageOperatorPair>();
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

            pairs.Add(new MeshImageOperatorPair( pair.Mesh, pair.Image));
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
        /// Area of original texture that is being used 
        /// </summary>
        private class TexturePatch
        {
            public HashSet<Triangle> triangles;
            public Image originalImage;
            public Image patchImage;
            BoundingBox uvBounds;

            public TexturePatch()
            {
                this.triangles = new HashSet<Triangle>();
            }

            /// <summary>
            /// Merge UV BoundingBox of given triang with border included with the bounding box of patch 
            /// </summary>
            /// <param name="t"></param>
            /// <param name="borderSize"></param>
            public void Add(Triangle t, int borderSize)
            {
                var uvBounds = t.UVBounds();
                uvBounds = originalImage.UVToPixel(uvBounds);
                uvBounds.Min.X = Math.Max(uvBounds.Min.X - borderSize, 0);
                uvBounds.Min.Y = Math.Max(uvBounds.Min.Y - borderSize, 0);
                uvBounds.Max.X = Math.Min(uvBounds.Max.X + borderSize, originalImage.Width - 1);
                uvBounds.Max.Y = Math.Min(uvBounds.Max.Y + borderSize, originalImage.Height - 1);
                uvBounds = originalImage.PixelToUv(uvBounds);
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
                this.patchImage = new Image(originalImage.Bands, (int)max.X - (int)min.X, (int)max.Y - (int)min.Y);
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
        /// Creates a list of TexturePatches by creating a TexturePatch from the original texture for every group of triangles whose UVBounds intersect,
        /// selecting which areas of the texture are being used
        /// </summary>
        /// <param name="mesh">input mesh with triangles of interest</param>
        /// <param name="img">original image</param>
        /// <param name="borderSize">pixel border around patches</param>
        /// <returns></returns>
        static private List<TexturePatch> ComputePatches(Mesh mesh, Image img, int borderSize)
        {
            mesh.Clean();
            MeshOperator op = new MeshOperator(mesh, buildFaceTree: false, buildVertexTree: false);
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
                    patch.originalImage = img;
                    Queue<Triangle> trianglesToProcess = new Queue<Triangle>();
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

        static public MeshImagePair RemapMeshClipImage(MeshOperator fullMeshOp, Mesh clippedMesh, Image image, int borderSize = 5, bool allowRotation = false)
        {
            return ClipPatches(ComputePatches(clippedMesh, image, borderSize), clippedMesh.HasNormals, clippedMesh.HasColors, image.Bands, borderSize, allowRotation);
        }

        static private MeshImagePair ClipPatches(List<TexturePatch> patches, bool hasNormals, bool hasColors, int outputImageBands, int borderSize, bool allowRotation)
        {
            int maxWidth = 0, maxHeight = 0;
            for (int b = 0; b < patches.Count; b++)
            {
                patches[b].ClipImage();
                maxWidth = Math.Max(maxWidth, patches[b].patchImage.Width);
                maxHeight = Math.Max(maxHeight, patches[b].patchImage.Height);
            }

            var binWidth = Math.Max(MathExtensions.MathE.CeilPowerOf2(maxWidth), 1);
            var binHeight = Math.Max(MathExtensions.MathE.CeilPowerOf2(maxHeight), 1);
            var binDepth = 1;

            Cuboid[] cuboids = new Cuboid[patches.Count];
            for (int i = 0; i < patches.Count; i++)
            {
                cuboids[i] = new Cuboid(patches[i].patchImage.Width, patches[i].patchImage.Height, 1, 0, patches[i]);
            }
            BinPackResult packed = null;
            var numBins = 0;

            //pack patches and adjust bin width and height until all patches packed into one bin
            while (numBins != 1)
            {
                var parameter = new BinPackParameter(binWidth, binHeight, binDepth, 0, allowRotation, cuboids);
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

            Image packedImg = new Image(outputImageBands, binWidth, binHeight);
            var cubes = packed.BestResult.First();
            //create new texture image from packing results
            foreach (var cube in cubes)
            {
                var patch = (TexturePatch)cube.Tag;
                bool rotate = false;
                if (patch.patchImage.Width != cube.Width || patch.patchImage.Height != cube.Height)
                {
                    rotate = true;
                    patch.patchImage = patch.patchImage.Rotate90Clockwise();
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
                //remap UV coordinates
                foreach (var t in patch.triangles)
                {
                    foreach (var v in t.Vertices())
                    {
                        Vector2 orignPixel = patch.originalImage.UVToPixel(v.UV);
                        Vector2 patchPixel = orignPixel - patch.MinPixel();
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
            MeshImagePair result = new MeshImagePair();
            result.Image = packedImg;
            result.Mesh = new Mesh(resultTriangles, hasNormals: hasNormals, hasUVs: true, hasColors: hasColors);
            return result;
        }

        /// <summary>
        /// Clips every mesh in the list of MeshImagePairs to specified bounding box. Creates new texture of patches from original images for each portion of clipped mesh packed into single image.
        /// Each patch has border of borderSize pixels. Returns new single MeshImagePair with clipped mesh and packed image.
        /// If rotation is allowed in packing, small pixel texture may be introduced.
        /// </summary>
        /// <param name="box"></param>
        /// <param name="borderSize"></param>
        /// <param name="allowRotation"></param>
        /// <returns></returns>
        public MeshImagePair Clip(BoundingBox box, int borderSize = 5, bool allowRotation = false)
        {
            if (allowRotation)
            {
                logger.Warn("Clip Mesh rotation is potentially unstable and may result in half pixel texture offsets");
            }
            List<TexturePatch> patches = new List<TexturePatch>();
        
            int outputImageBands = 0;
            bool hasNormals = false;
            bool hasColors = false;
            foreach (var pair in pairs)
            {
                Mesh clippedMesh = pair.MeshOperator.Clip(box);
                clippedMesh.Clean();
                patches.AddRange(ComputePatches(clippedMesh, pair.Image, borderSize));
                outputImageBands = Math.Max(outputImageBands, pair.Image.Bands);
                hasNormals |= pair.MeshOperator.HasNormals;
                hasColors |= pair.MeshOperator.HasColors;

            }

            return ClipPatches(patches, hasNormals, hasColors, outputImageBands, borderSize, allowRotation);

        }
    }
}
