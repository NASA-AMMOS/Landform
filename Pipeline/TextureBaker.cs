using OPS.Geometry;
using OPS.Imaging;
using OPS.Pipeline;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using RTree;
using log4net;
using System.Threading;
using static OPS.Geometry.Triangle;

namespace OPS.Pipeline
{

    public class TextureBaker
    {
        // pixel width of in paint padding
        const int PAD = 10;

        /// <summary>
        /// Return trues if at least one of 4 neighbors of position (r, c) in image is unmasked
        /// </summary>
        /// <param name="r"></param>
        /// <param name="c"></param>
        /// <param name="image"></param>
        /// <returns></returns>
        public static bool HasNeighbors(int r, int c, Image image)
        {
            if (r > 0 && !image.IsMasked(r-1,c))
                return true;
            if (c > 0 && !image.IsMasked(r,c-1))
                return true;
            if (r < image.Height-1 && !image.IsMasked(r+1,c))
                return true;
            if (c < image.Width-1 && !image.IsMasked(r,c+1))
                return true;
            return false;
        }

        /// <summary>
        /// Write average of up to 8 non null neighbor pixels in readImage to position (r, c) in writeImage 
        /// </summary>
        /// <param name="r"></param>
        /// <param name="c"></param>
        /// <param name="readImage"></param>
        /// <param name="writeImage"></param>
        public static void Pad(int r, int c, Image readImage, Image writeImage)
        {
            float num = 0;
            float[] average = new float[readImage.Bands];
            for (int b = 0; b < readImage.Bands; b++)
                average[b] = 0;
            if (r > 0 && !readImage.IsMasked(r-1,c))
            {
                num++;
                for(int b = 0; b < readImage.Bands; b++)
                    average[b] += readImage.GetBandValues(r - 1,c)[b];
            }
            if (c > 0 && !readImage.IsMasked(r, c - 1))
            {
                num++;
                for (int b = 0; b < readImage.Bands; b++)
                    average[b] += readImage.GetBandValues(r, c - 1)[b];
            }
            if (r < readImage.Height - 1 && !readImage.IsMasked(r + 1, c))
            {
                num++;
                for (int b = 0; b < readImage.Bands; b++)
                    average[b] += readImage.GetBandValues(r + 1, c)[b];
            }
            if (c < readImage.Width - 1 && !readImage.IsMasked(r, c + 1))
            {
                num++;
                for (int b = 0; b < readImage.Bands; b++)
                    average[b] += readImage.GetBandValues(r, c + 1)[b];
            }

            if (r > 0 && c > 0 && !readImage.IsMasked(r - 1, c - 1))
            {
                num++;
                for (int b = 0; b < readImage.Bands; b++)
                    average[b] += readImage.GetBandValues(r - 1, c - 1)[b];
            }
            if (r > 0 && c < readImage.Width-1 && !readImage.IsMasked(r - 1, c + 1))
            {
                num++;
                for (int b = 0; b < readImage.Bands; b++)
                    average[b] += readImage.GetBandValues(r - 1, c + 1)[b];
            }
            if (r < readImage.Height-1 && c > 0 && !readImage.IsMasked(r + 1, c - 1))
            {
                num++;
                for (int b = 0; b < readImage.Bands; b++)
                    average[b] += readImage.GetBandValues(r + 1, c - 1)[b];
            }
            
            if (r < readImage.Height-1 && c < readImage.Width-1 && !readImage.IsMasked(r + 1, c + 1))
            {
                num++;
                for (int b = 0; b < readImage.Bands; b++)
                    average[b] += readImage.GetBandValues(r + 1, c + 1)[b];
            }

            for (int b = 0; b < readImage.Bands; b++)
                average[b] /= num;
            writeImage.SetBandValues(r, c, average);
        }

        public static Image BakeTexture(MeshImagePair[] source, Mesh dest, int destWidth, int destHeight)
        {
            // r tree for efficient uv to xyz conversion
            var destOperator = new MeshOperator(dest);

            int numSources = source.Count();
            if (numSources == 0)
                return null;

            // the new texture
            var destImage = new Image(source[0].Image.Bands, destWidth, destHeight);

            // compute union of source mesh bounds
            double minX = double.MaxValue;
            double minY = double.MaxValue;
            double minZ = double.MaxValue;
            double maxX = double.MinValue;
            double maxY = double.MinValue;
            double maxZ = double.MinValue;

            for(int i = 0; i < numSources; i++)
            {
                var box = source[i].Mesh.Bounds();
                minX = Math.Min(minX, box.Min.X);
                minY = Math.Min(minY, box.Min.Y);
                minZ = Math.Min(minZ, box.Min.Z);
                maxX = Math.Max(maxX, box.Max.X);
                maxY = Math.Max(maxY, box.Max.Y);
                maxZ = Math.Max(maxZ, box.Max.Z);
            }

            // construct oct tree on source meshes
            
            BoundingBox finalBox = new BoundingBox(new Vector3(minX, minY, minZ), new Vector3(maxX, maxY, maxZ));
            TriangleOctTree triOctTree = new TriangleOctTree(finalBox);
            for(int i = 0; i < numSources; i++)
            {
                triOctTree.InsertMesh(source[i].Mesh, source[i].Image);
            }

            Node start = triOctTree.Root;
            Node end;

            destImage.CreateMask(true);

            // compute nearest neighbor for each dest pixel
            for (int r = 0; r < destImage.Height; r++)
            {
                for(int c = 0; c < destImage.Width; c++)
                {
                    // get the xyz coordinate in the new mesh
                    Vector2 uvDest = destImage.PixelToUV(new Vector2(c, r));
                    Vector3? xyzDest = destOperator.UVToPosition(uvDest);
                    Barycentric closest = null;
                    TexturedTriangle txtTri = null;
                    // find its nearest neighbor in the old mesh, and save its location in the tree as start node for next search
                    if (xyzDest.HasValue)
                    {
                        txtTri = triOctTree.Closest(xyzDest.Value, start, out end);
                        start = end;
                        closest = txtTri.tri.ClosestPointOnTriangle(xyzDest.Value);
                    }
                    // Sample the old texture at this point
                    if (closest != null)
                    {
                        Image image = txtTri.texture;
                        Vector2 pixel = image.UVToPixel(closest.UV);
            
                        float row = (float)pixel.Y;
                        float col = (float)pixel.X;
                        var bands = new float[image.Data.Count()];
                        for (int b = 0; b < bands.Count(); b++)
                        {
                            bands[b] = image.BicubicSample(b, row, col);
                        }                       
                        destImage.SetBandValues(r, c, bands);
                        destImage.SetMaskValue(r, c, false);
                    }
                }
            }         

            // in paint set up:
            //   create copy of image
            //   add "edge points" to the mask of image, and store them in a new list
            Image destImageCopy = (Image)destImage.Clone();

            List<Vector2> edgePoints = new List<Vector2>();
            List<Vector2> newEdgePoints;

            for (int r = 0; r < destImage.Height; r++)
            {
                for (int c = 0; c < destImage.Width; c++)
                {
                    if (HasNeighbors(r, c, destImageCopy) && destImageCopy.IsMasked(r, c))
                    {
                        destImage.SetMaskValue(r, c, false);
                        edgePoints.Add(new Vector2(r, c));
                    }
                }
            }

            // in paint:
            //   Use copy to populate current edge points in destImage
            //   Use edge points to get new list of edge points and continue padding outwards
            for (int i = 0; i < PAD; i++)
            {
                foreach (Vector2 edge in edgePoints)
                {
                    Pad((int)edge.X, (int)edge.Y, destImageCopy, destImage);
                }
                destImageCopy = (Image)destImage.Clone();
                newEdgePoints = new List<Vector2>();
                foreach (Vector2 edge in edgePoints)
                {
                    int r = (int)edge.X;
                    int c = (int)edge.Y;
                    if(r > 0 && destImage.IsMasked(r-1,c))
                    {
                        destImage.SetMaskValue(r-1, c, false);
                        newEdgePoints.Add(new Vector2(r-1, c));
                    }
                    if (c > 0 && destImage.IsMasked(r, c-1))
                    {
                        destImage.SetMaskValue(r, c-1, false);
                        newEdgePoints.Add(new Vector2(r, c-1));
                    }
                    if (r < destImage.Height - 1 && destImage.IsMasked(r + 1, c))
                    {
                        destImage.SetMaskValue(r + 1, c, false);
                        newEdgePoints.Add(new Vector2(r + 1, c));
                    }
                    if (c < destImage.Width-1 && destImage.IsMasked(r, c + 1))
                    {
                        destImage.SetMaskValue(r, c + 1, false);
                        newEdgePoints.Add(new Vector2(r, c + 1));
                    }
                }
                edgePoints = newEdgePoints;
            }

            return destImage;
        } 
    }
}
