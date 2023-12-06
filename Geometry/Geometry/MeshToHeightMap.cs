using Microsoft.Xna.Framework;
using JPLOPS.Imaging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace JPLOPS.Geometry
{
    public static class MeshToHeightMap
    {
        const float BIGGY = 1000000000;

        public static Image BuildDem(Mesh mesh, int targetRes, out double metersPerPixel, out double xOffset, out double yOffset)
        {
            var initSceneBounds = mesh.Bounds();
            double initXDimMeters = initSceneBounds.Max.X - initSceneBounds.Min.X;
            double initYDimMeters = initSceneBounds.Max.Y - initSceneBounds.Min.Y;
            metersPerPixel = Math.Sqrt(initXDimMeters * initYDimMeters) / targetRes;
            double preClipXOffset = (initSceneBounds.Max.X + initSceneBounds.Min.X) / 2.0;
            double preClipYOffset = (initSceneBounds.Max.Y + initSceneBounds.Min.Y) / 2.0;
            int xDimPixels = (int)Math.Ceiling(initXDimMeters / metersPerPixel);
            int yDimPixels = (int)Math.Ceiling(initYDimMeters / metersPerPixel);
            double xDimMeters = xDimPixels * metersPerPixel;
            double yDimMeters = yDimPixels * metersPerPixel;
            BoundingBox sceneBounds = new BoundingBox(new Vector3(preClipXOffset - xDimMeters / 2.0, preClipYOffset - yDimMeters / 2.0, 0),
                                                      new Vector3(preClipXOffset + xDimMeters / 2.0, preClipYOffset + yDimMeters / 2.0, 0));
            var ret = BuildDem(mesh, sceneBounds, xDimPixels, yDimPixels);
            int sbs = 1;
            ret.InvalidateSparseExternalBlocks(sbs, 0.5);
            ret.InvalidateAllButLargestValidBlobs();
            int oldW = ret.Width;
            int oldH = ret.Height;
            ret = ret.Trim(out Vector2 ulc);
            double centerDriftCols = ulc.X + ret.Width / 2.0 - oldW / 2.0;
            double centerDriftRows = ulc.Y + ret.Height / 2.0 - oldH / 2.0;

            xOffset = preClipXOffset - centerDriftRows * metersPerPixel;
            yOffset = preClipYOffset + centerDriftCols * metersPerPixel;

            return ret;
        }

        /// <summary>
        /// Special case to invert dem2mesh coordinate transform
        /// </summary>
        /// <param name="mesh"></param>
        /// <param name="bounds"></param>
        /// <param name="yDimPixels"></param>
        /// <param name="xDimPixels"></param>
        /// <returns></returns>
        public static Image BuildDem(Mesh mesh, BoundingBox bounds, int xDimPixels, int yDimPixels)
        {
            //Create a deep copy of the mesh
            Mesh mesh_copy = new Mesh(mesh);

            //Set UVs to be projection into xy plane
            mesh_copy.Vertices.ForEach(vert => {
                vert.UV = new Vector2(vert.Position.X, vert.Position.Y);
            });
            mesh_copy.HasUVs = true;

            //Build a mesh operator for efficient point look up
            MeshOperator mo = new MeshOperator(mesh_copy, buildFaceTree: false, buildVertexTree: false, buildUVFaceTree: true);

            //For each image pixel, find projected location in triangle and interpolate height, or mask out
            //We flip x and y when building mesh. Here we flip back in creating dem
            int imageHeight = xDimPixels;
            int imageWidth = yDimPixels;
            Image heightmap = new Image(1, imageWidth, imageHeight);
            heightmap.CreateMask();
            double minX = bounds.Min.X;
            double minY = bounds.Min.Y;
            double xExtent = bounds.Max.X - minX;
            double yExtent = bounds.Max.Y - minY;
            for (int r = 0; r < imageHeight; r++)
            {
                for (int c = 0; c < imageWidth; c++)
                {
                    // scene +X = North = -row in dem
                    // scene +Y = East  =  col in dem
                    // x increases with (width - r - 1), y increases with c
                    double y = minY + c * yExtent / (double)imageWidth;
                    double x = minX + (imageHeight - r - 1) * xExtent / (double)imageHeight;
                    List<BarycentricPoint> points = mo.UVToBarycentricList(new Vector2(x, y)).ToList();
                    if (points.Count == 0)
                    {
                        heightmap[0, r, c] = BIGGY;
                        heightmap.SetMaskValue(r, c, true);
                    }
                    else
                    {
                        heightmap[0, r, c] = -1 * (float)points.Select(v => v.Position.Z).Min(); //Find highest point assuming +Z is gravity
                        heightmap.SetMaskValue(r, c, false);
                    }
                }
            }

            return heightmap;
        }

        public static Image BuildHeightMap(Mesh mesh, int width, int height,
                                           VertexProjection.ProjectionAxis axis, bool invertHeight = false)
        {
            mesh = new Mesh(mesh);
            var getUV = VertexProjection.MakeUVProjector(axis);
            mesh.Vertices.ForEach(v => { v.UV = getUV(v.Position); });
            mesh.HasUVs = true;
            var mo = new MeshOperator(mesh, buildFaceTree: false, buildVertexTree: false, buildUVFaceTree: true);
            return BuildHeightMap(mo, width, height, axis, invertHeight);
        }

        public static Image BuildHeightMap(MeshOperator mo, int width, int height,
                                           VertexProjection.ProjectionAxis axis, bool invertHeight = false)
        {
            var getUV = VertexProjection.MakeUVProjector(axis);
            var getHeight = VertexProjection.MakeHeightGetter(axis);

            //For each image pixel, find projected location in triangle and interpolate height, or mask out
            Image heightmap = new Image(1, width, height);
            heightmap.CreateMask();
            var bounds = mo.Bounds;
            Vector2 min = getUV(bounds.Min);
            Vector2 max = getUV(bounds.Max);
            double minU = min.U;
            double minV = min.V;
            double uExtent = max.U - minU;
            double vExtent = max.V - minV;
            for (int r = 0; r < height; r++)
            {
                for (int c = 0; c < width; c++)
                {
                    double u = minU + c * uExtent / (double)width;
                    double v = minV + (height - r - 1) * vExtent / (double)height;
                       
                    List<BarycentricPoint> points = mo.UVToBarycentricList(new Vector2(u, v)).ToList();
                    if (points.Count == 0)
                    {
                        heightmap[0, r, c] = BIGGY;
                        heightmap.SetMaskValue(r, c, true);
                    }
                    else
                    {
                        if (invertHeight)
                        {
                            heightmap[0, r, c] = -1 * (float)points.Select(vert => getHeight(vert.Position)).Min();
                        }
                        else
                        {
                            heightmap[0, r, c] = (float)points.Select(vert => getHeight(vert.Position)).Max();
                        }
                        heightmap.SetMaskValue(r, c, false);
                    }
                }
            }
            return heightmap;
        }
    }
}
