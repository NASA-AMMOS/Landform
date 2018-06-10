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
using OPS.MathExtensions;

namespace OPS.Pipeline
{
    public static class TextureBaker
    {
        /// <summary>
        /// Returns the total texture area in pixels covered by this mesh
        /// </summary>
        /// <param name="mesh"></param>
        /// <param name="image"></param>
        /// <returns></returns>
        public static double ComputePixelArea(Mesh mesh, Image image)
        {
            if(image == null || !mesh.HasUVs)
            {
                return 0;
            }
            double totalPixels = 0;
            var triangles = mesh.Triangles();
            foreach (var t in triangles)
            {
                Vector3 a = new Vector3(image.UVToPixel(t.V0.UV), 0);
                Vector3 b = new Vector3(image.UVToPixel(t.V1.UV), 0);
                Vector3 c = new Vector3(image.UVToPixel(t.V2.UV), 0);
                var pixelTri = new Triangle(a, b, c);
                if (double.IsNaN(pixelTri.Area()))
                {
                    throw new Exception("Triangle area not a number");
                }
                totalPixels += pixelTri.Area();
            }
            return totalPixels;
        }

        /// <summary>
        /// Given an area measured in pixels squared, return the dimension (width/height) of the smallest square image
        /// large enough to contain that area
        /// </summary>
        /// <param name="areaInPixels"></param>
        /// <returns></returns>
        public static int PixelAreaToSquareDimension(double areaInPixels)
        {
            if(areaInPixels == 0)
            {
                return 0;
            }
            double size = Math.Sqrt(areaInPixels);
            size = MathE.CeilPowerOf2(size);
            size = Math.Min(size, areaInPixels);
            return (int)size;
        }

        public static Image BakeTexture(MeshImagePair[] source, Mesh dest, int destWidth, int destHeight, int padWidth = -1)
        {
            // r tree for efficient uv to xyz conversion
            var destOperator = new MeshOperator(dest);

            int numSources = source.Count();
            if (numSources == 0)
                return null;

            // the new texture
            var destImage = new Image(source[0].Image.Bands, destWidth, destHeight);

            // Get union bounding box of source meshes
            List<BoundingBox> boxes = new List<BoundingBox>();
            foreach (MeshImagePair pair in source)
            {
                boxes.Add(pair.Mesh.Bounds());
            }
            BoundingBox finalBox = BoundingBoxExtensions.Union(boxes.ToArray());

            // construct oct tree on source meshes
            Octree triOctTree = new Octree(finalBox);
            for(int i = 0; i < numSources; i++)
            {
                List<OctreeNodeContents> insertList = new List<OctreeNodeContents>();
                foreach(Triangle tri in source[i].Mesh.Triangles())
                {
                    insertList.Add(new TexturedTriangle(tri, source[i].Image));
                }
                triOctTree.InsertList(insertList);
            }

            OctreeNode start = triOctTree.Root;
            OctreeNode end;

            destImage.CreateMask(true);

            // compute nearest neighbor for each dest pixel
            for (int r = 0; r < destImage.Height; r++)
            {
                for(int c = 0; c < destImage.Width; c++)
                {
                    // get the xyz coordinate in the new mesh
                    Vector2 uvDest = destImage.PixelToUV(new Vector2(c, r));
                    BarycentricPoint bp = destOperator.UVToBarycentric(uvDest);
                    Vector3? xyzDest = (bp != null) ? (Vector3?)bp.Position : null;
                    BarycentricPoint closest = null;
                    TexturedTriangle txtTri = null;
                    // find its nearest neighbor in the old mesh, and save its location in the tree as start node for next search
                    if (xyzDest.HasValue)
                    {
                        txtTri = (TexturedTriangle)triOctTree.Closest(xyzDest.Value, start, out end);
                        start = end;
                        closest = txtTri.tri.ClosestPoint(xyzDest.Value);
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

            // in paint
            destImage.Inpaint(padWidth);
            return destImage;
        } 
    }
}
