using Microsoft.Xna.Framework;
using OPS.Imaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Geometry
{
    public static class MeshToHeightMap
    {
        public static Tuple<Image, Mask> BuildHeightMap(Mesh mesh, BoundingBox bounds, int width, int height)
        {
            //Create a deep copy of the mesh
            Mesh mesh_copy = new Mesh();
            mesh_copy.Faces = mesh.Faces;
            mesh_copy.Vertices = mesh.Vertices.Select(v => { return new Vertex(v); }).ToList();

            //Set UVs to be projection into xy plane
            mesh_copy.Vertices.ForEach(vert => {
                vert.UV = new Vector2(vert.Position.X, vert.Position.Y);
            });
            mesh_copy.HasUVs = true;

            //Build a mesh operator for efficient point look up
            MeshOperator mo = new MeshOperator(mesh_copy, buildFaceTree : false, buildVertexTree : false, buildUVFaceTree : true);
            
            //For each image pixel, find projected location in triangle and interpolate height, or mask out
            Image heightmap = new Image(1, width, height);
            Mask mask = new Mask(width, height, false);
            float BIGGY = 1000000000;
            double minX = bounds.Min.X;
            double minY = bounds.Min.Y;
            double xExtent = bounds.Max.X - minX;
            double yExtent = bounds.Max.Y - minY;
            for (int r = 0; r < width; r++)
            {
                for(int c = 0; c < height; c++)
                {
                    double y = minY + c * yExtent / (double)height;
                    double x = minX + (width - r - 1) * xExtent / (double)width;
                    BarycentricPoint p = mo.UVToBarycentric(new Vector2(x, y));
                    if (p == null)
                    {
                        heightmap[0, r, c] = BIGGY;
                        mask.setInvalid(r, c);
                    }
                    else
                    {
                        heightmap[0, r, c] = -1 * (float)p.Position.Z;
                        mask.setValid(r, c);
                    }
                }
            }

            return new Tuple<Image, Mask>(heightmap, mask);
        }
    }
}
