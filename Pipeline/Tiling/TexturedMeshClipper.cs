using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sharp3DBinPacking;
using OPS.Geometry;
using OPS.Imaging;

namespace OPS.Pipeline
{
    public class TexturedMeshClipper
    {
        public class TexturePatch
        {
            public BoundingBox box;
            public List<Triangle> triangles;

            public TexturePatch(BoundingBox box, List<Triangle> triangles)
            {
                this.box = box;
                this.triangles = triangles;
            }

        }

        /// <summary>
        /// Given a mesh and image, returns a mesh clipped to the clipping bounds and an image containing texture data for that portion of the mesh
        /// The returned image may be repacked to fit in a smaller texture
        /// </summary>
        /// <param name="inputPair"></param>
        /// <param name="clipBounds"></param>
        /// <returns></returns>
        public MeshImagePair ClipMesh(MeshImagePair inputPair, BoundingBox clipBounds)
        {
            var result = new MeshImagePair();
            Image img = inputPair.Image;
            MeshOperator meshOperator = new MeshOperator(inputPair.Mesh);
            result.Mesh = meshOperator.Clip(clipBounds);
            List<TexturePatch> unionBoxes = new List<TexturePatch>();
            foreach (var t in result.Mesh.Triangles())
            {
                TexturePatch uvbox = new TexturePatch(t.UVBounds(), new List<Triangle> { t });
                for (int i = 0; i < unionBoxes.Count; i++)
                {
                    if (uvbox.box.Intersects(unionBoxes[i].box))
                    {
                        uvbox.box = BoundingBoxExtensions.Union(uvbox.box, unionBoxes[i].box);
                        uvbox.triangles.AddRange(unionBoxes[i].triangles);
                        unionBoxes.RemoveAt(i--);
                    }
                }
                unionBoxes.Add(uvbox);
            }

            int clippedArea = 0;
            for (int b = 0; b < unionBoxes.Count; b++)
            {
                unionBoxes[b].box = img.UVToPixel(unionBoxes[b].box);
                clippedArea += (int)(BoundingBoxExtensions.Size(unionBoxes[b].box).X * BoundingBoxExtensions.Size(unionBoxes[b].box).Y);
                //for (int i = 0; i < img.Bands; i++)
                //{
                //    for (int j = (int)boxes[b].Min.Y; j < boxes[b].Max.Y; j++)
                //    {
                //        for (int k = (int)boxes[b].Min.X; k < boxes[b].Max.X; k++)
                //        {
                //            img[i, j, k] = 1;
                //        }
                //    }
                //}
            }

            var binWidth = MathExtensions.MathE.CeilPowerOf2(Math.Sqrt(clippedArea));
            var binHeight = binWidth;
            var binDepth = 0.5m;

            Cuboid[] cuboids = new Cuboid[unionBoxes.Count];
            for (int i = 0; i < unionBoxes.Count; i++)
            {
                cuboids[i] = new Cuboid((int)(unionBoxes[i].box.Max.X - unionBoxes[i].box.Min.X), (int)(unionBoxes[i].box.Max.Y - unionBoxes[i].box.Min.Y), 0.5m);
            }
            var parameter = new BinPackParameter(binWidth, binHeight, binDepth, cuboids);

            // Create a bin packer instance
            // The default bin packer will test all algorithms and try to find the best result
            // BinPackerVerifyOption is used to avoid bugs, it will check whether the result is correct
            var binPacker = BinPacker.GetDefault(BinPackerVerifyOption.BestOnly);

            // The result contains bins which contains packed cuboids whith their coordinates
            var packed = binPacker.Pack(parameter);
            Image packedImg = new Image(img.Bands, binWidth, binHeight);
            foreach (var bins in packed.BestResult)
            {
                for (int patch = 0; patch < bins.Count; patch++)
                {
                    for (int boxNum = 0; boxNum < unionBoxes.Count; boxNum++)
                    {
                        if ((int)BoundingBoxExtensions.Size(unionBoxes[boxNum].box).X == bins[patch].Width && (int)BoundingBoxExtensions.Size(unionBoxes[boxNum].box).Y == bins[patch].Height)
                        {
                            foreach (var t in unionBoxes[boxNum].triangles)
                            {
                                Vector2 offset = new Vector2((double)bins[patch].X - unionBoxes[boxNum].box.Min.X, (double)bins[patch].Y - unionBoxes[boxNum].box.Min.Y);
                                t.V0.UV = packedImg.PixelToUV(img.UVToPixel(t.V0.UV) + offset);
                                t.V1.UV = packedImg.PixelToUV(img.UVToPixel(t.V1.UV) + offset);
                                t.V2.UV = packedImg.PixelToUV(img.UVToPixel(t.V2.UV) + offset);
                            }
                            for (int i = 0; i < bins[patch].Height; i++)
                                {
                                    for (int j = 0; j < bins[patch].Width; j++)
                                    {
                                        packedImg.SetBandValues(i + (int)bins[patch].Y, j + (int)bins[patch].X, img.GetBandValues((int)unionBoxes[boxNum].box.Min.Y + i, (int)unionBoxes[boxNum].box.Min.X + j));
                                    }
                                }
                            unionBoxes.RemoveAt(boxNum);
                            break;
                        }
                        else if ((int)BoundingBoxExtensions.Size(unionBoxes[boxNum].box).X == bins[patch].Height && (int)BoundingBoxExtensions.Size(unionBoxes[boxNum].box).Y == bins[patch].Width)
                        {
                            foreach (var t in unionBoxes[boxNum].triangles)
                            {
                                Vector2 offset = new Vector2((double)bins[patch].X - unionBoxes[boxNum].box.Min.X, (double)bins[patch].Y - unionBoxes[boxNum].box.Min.Y);

                            }
                            for (int i = 0; i < bins[patch].Height; i++)
                            {
                                for (int j = 0; j < bins[patch].Width; j++)
                                {
                                    packedImg.SetBandValues(i + (int)bins[patch].Y, j + (int)bins[patch].X, img.GetBandValues((int)unionBoxes[boxNum].box.Max.Y - j, (int)unionBoxes[boxNum].box.Min.X + i));
                                }
                            }
                            unionBoxes.RemoveAt(boxNum);
                            break;
                        }
                    }
                }
                Console.WriteLine("Bin:");
                foreach (var cuboid in bins)
                {
                    Console.WriteLine(cuboid);
                }
            }

            result.Image = packedImg;
            return result;
        }

    }
}
