using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using JPLOPS.MathExtensions;
using JPLOPS.Imaging;

namespace JPLOPS.Geometry
{
    public enum Neighborhood { Four = 4, Eight = 8 };

    public enum TiltMode { None, Abs, Acos, InvAcos, Cos };

    public class OrganizedPointCloud
    {
        public const Neighborhood DEF_CURVATURE_NEIGHBORHOOD = Neighborhood.Four;
        public const TiltMode DEF_TILT_MODE = TiltMode.InvAcos;

        /// <summary>
        /// compute approximate max abs curvature at each valid point
        /// uses XNAExtensions.Curvature()
        /// </summary>
        public static Image Curvatures(Image points, Image normals, bool normalize = true,
                                       Neighborhood neighborhood = DEF_CURVATURE_NEIGHBORHOOD)
        {
            int hoodSize = (int)neighborhood + 1;
            Pixel[] offsets = new Pixel[hoodSize];
            offsets[0] = new Pixel(0, 0);
            offsets[1] = new Pixel(-1, 0);
            offsets[2] = new Pixel(1, 0);
            offsets[3] = new Pixel(0, -1);
            offsets[4] = new Pixel(0, 1);
            if (neighborhood == Neighborhood.Eight)
            {
                offsets[5] = new Pixel(-1, -1);
                offsets[6] = new Pixel(1, 1);
                offsets[7] = new Pixel(-1, 1);
                offsets[8] = new Pixel(1, -1);
            }
            var hoodPoints = new Vector3[hoodSize];
            var hoodNorms = new Vector3[hoodSize];
            int collectHood(int row, int col)
            {
                var ctr = new Pixel(row, col);
                int n = 0;
                foreach (var offset in offsets)
                {
                    var px = ctr + offset;
                    if (points.IsValid(px.Row, px.Col) && normals.IsValid(px.Row, px.Col))
                    {
                        hoodPoints[n] = new Vector3(points[0, px.Row, px.Col],
                                                    points[1, px.Row, px.Col],
                                                    points[2, px.Row, px.Col]);
                        hoodNorms[n] = new Vector3(normals[0, px.Row, px.Col],
                                                   normals[1, px.Row, px.Col],
                                                   normals[2, px.Row, px.Col]);
                        n++;
                    }
                }
                return n;
            }

            Image ret = new Image(1, points.Width, points.Height);
            ret.CreateMask();

            float min = float.PositiveInfinity;
            float max = float.NegativeInfinity;
            for (int row = 0; row < ret.Height; row++)
            {
                for (int col = 0; col < ret.Width; col++)
                {
                    if (points.IsValid(row, col) && normals.IsValid(row, col))
                    {
                        int n = collectHood(row, col);
                        float maxAbsCurvature = 0;
                        for (int i = 1; i < n; i++)
                        {
                            var c = (float)Math.Abs(XNAExtensions.Curvature(hoodPoints[0], hoodPoints[i],
                                                                            hoodNorms[0], hoodNorms[i]));
                            maxAbsCurvature = Math.Max(maxAbsCurvature, c);
                        }
                        ret[0, row, col] = maxAbsCurvature;
                        min = Math.Min(min, maxAbsCurvature);
                        max = Math.Max(max, maxAbsCurvature);
                    }
                    else
                    {
                        ret.SetMaskValue(row, col, true);
                    }
                }
            }

            if (normalize)
            {
                ret.ScaleValues(min, max, 0, 1);
            }

            return ret;
        }

        /// <summary>
        /// convert a points image to a scalar elevation image  
        /// up direction defaults to (0, 0, -1)
        /// </summary>
        public static Image Elevations(Image img, bool normalize = true, bool absolute = false, Vector3? up = null)
        {
            if (up == null)
            {
                up = new Vector3(0, 0, -1);
            }

            Image ret = new Image(1, img.Width, img.Height);
            ret.CreateMask();

            var ctr = new Vector3(0, 0, 0);

            if (!absolute)
            {
                BoundingBox bounds = new BoundingBox(Vector3.Largest, Vector3.Smallest);
                for (int row = 0; row < img.Height; row++)
                {
                    for (int col = 0; col < img.Width; col++)
                    {
                        if (img.IsValid(row, col))
                        {
                            var p = new Vector3(img[0, row, col], img[1, row, col], img[2, row, col]);
                            bounds.Min = Vector3.Min(bounds.Min, p);
                            bounds.Max = Vector3.Max(bounds.Max, p);
                        }
                    }
                }
                ctr = bounds.Center();
            }

            float min = float.PositiveInfinity;
            float max = float.NegativeInfinity;
            for (int row = 0; row < img.Height; row++)
            {
                for (int col = 0; col < img.Width; col++)
                {
                    if (img.IsValid(row, col))
                    {
                        var p = new Vector3(img[0, row, col], img[1, row, col], img[2, row, col]);
                        var elev = (float)(p - ctr).Dot(up.Value);
                        ret[0, row, col] = elev;
                        min = Math.Min(min, elev);
                        max = Math.Max(max, elev);
                    }
                    else
                    {
                        ret.SetMaskValue(row, col, true);
                    }
                }
            }

            if (normalize)
            {
                ret.ScaleValues(min, max, 0, 1);
            }

            return ret;
        }

        /// <summary>
        /// TiltMode.Abs: tilt is the absolute value of the cosine of the angle relative to up
        /// TiltMode.Acos: tilt is the angle relative to up normalized to 0-1
        /// TiltMode.InvAcos: tilt is the angle relative to down normalized to 0-1
        /// TiltMode.Cos: tilt is cosine of the angle relative to up
        /// </summary>
        public static double NormalToTilt(Vector3 n, TiltMode mode, Vector3 up)
        {
            var tilt = MathE.Clamp01(n.Dot(up));
            switch (mode)
            {
                case TiltMode.Abs: tilt = Math.Abs(tilt); break;
                case TiltMode.Acos: tilt = Math.Acos(tilt) / Math.PI; break;
                case TiltMode.InvAcos: tilt = 1 - Math.Acos(tilt) / Math.PI; break;
                case TiltMode.Cos: break;
                default: throw new ArgumentException("unhandled tilt mode: " + mode);
            }
            return tilt;
        }

        /// <summary>
        /// Convert a normals vector image to a scalar "tilt" image  
        /// up defaults to (0, 0, -1)
        /// </summary>
        public static Image NormalsToTilt(Image img, TiltMode tiltMode = DEF_TILT_MODE, Vector3? up = null)
        {
            if (up == null)
            {
                up = new Vector3(0, 0, -1);
            }

            Image ret = new Image(1, img.Width, img.Height);
            ret.CreateMask();

            for (int row = 0; row < img.Height; row++)
            {
                for (int col = 0; col < img.Width; col++)
                {
                    if (img.IsValid(row, col))
                    {
                        var n = new Vector3(img[0, row, col], img[1, row, col], img[2, row, col]);
                        ret[0, row, col] = (float)NormalToTilt(n, tiltMode, up.Value);
                    } 
                    else
                    {
                        ret.SetMaskValue(row, col, true);
                    }
                }
            }

            return ret;
        }

        /// <summary>
        /// mask and decimate a normals image   
        /// if mask image is provided then any pixels which are 0 there are ignored
        /// </summary>
        public static Image MaskAndDecimateNormals(Image img, int blocksize, Image mask = null, bool normalize = false)
        {
            if (mask != null)
            {
                img.UnionMask(mask, new float[] { 0 });
            }
            if (blocksize > 1)
            {
                img = img.Decimated(blocksize);
            }
            if (normalize)
            {
                for (int row = 0; row < img.Height; row++)
                {
                    for (int col = 0; col < img.Width; col++)
                    {
                        if (img.IsValid(row, col))
                        {
                            var n = new Vector3(img[0, row, col], img[1, row, col], img[2, row, col]);
                            if (n.LengthSquared() < 0.0001)
                            {
                                img.SetMaskValue(row, col, true);
                            }
                            else
                            {
                                n.Normalize();
                                img.SetBandValues(row, col, n.ToFloatArray());
                            }
                        }
                    }
                }
            }
            return img;
        }

        /// <summary>
        /// decimate a points image, baking mask into it
        /// if mask image is provided then any pixels which are 0 there are ignored
        /// </summary>
        public static Image MaskAndDecimatePoints(Image img, int blocksize, Image mask = null)
        {
            if (mask != null)
            {
                img.UnionMask(mask, new float[] { 0 });
            }
            return blocksize > 1 ? img.Decimated(blocksize) : img;
        }

        /// <summary>
        /// if mask image is provided then any pixels which are 0 there are ignored
        /// </summary>
        public static Mesh BuildPointCloudMesh(Image points, Image normals = null, Image mask = null)
        {
            if (points == null)
            {
                return null;
            }
            Mesh ret = new Mesh(hasNormals: normals != null);
            for (int row = 0; row < points.Height; row++)
            {
                for (int col = 0; col < points.Width; col++)
                {
                    if (!points.IsValid(row, col) || (normals != null && !normals.IsValid(row, col)) ||
                        mask != null && mask[0, row, col] == 0)
                    {
                        continue;
                    }
                    var v = new Vertex(new Vector3(points[0, row, col], points[1, row, col], points[2, row, col]));
                    if (normals != null)
                    {
                        v.Normal = new Vector3(normals[0, row, col], normals[1, row, col], normals[2, row, col]);
                    }
                    ret.Vertices.Add(v);
                }
            }
            return ret;
        }

        private struct TriFace
        {
            public int r0, c0, r1, c1, r2, c2;

            public TriFace(int r0, int c0, int r1, int c1, int r2, int c2)
            {
                this.r0 = r0;
                this.c0 = c0;
                this.r1 = r1;
                this.c1 = c1;
                this.r2 = r2;
                this.c2 = c2;
            }
        }

        /// <summary>
        /// build a mesh from the given points and optional normals and mask images
        /// if mask image is provided then any pixels which are 0 there are ignored
        /// generateNormals = true means generate normals iff the supplied normals image is null
        /// </summary>
        public static Mesh BuildOrganizedMesh(Image points, Image normals = null, Image mask = null,
                                              double maxTriangleAspect = 20,
                                              bool generateUV = true, bool generateNormals = true,
                                              Vector3? flipGeneratedNormalsToward = null,
                                              double isolatedPointSize = 0, bool reverseWinding = false,
                                              bool quadsOnly = false)
        {
            if (points == null)
            {
                return null;
            }

            if (maxTriangleAspect < 1)
            {
                throw new ArgumentException("max triangle aspect must be >= 1");
            }

            Mesh ret = new Mesh(hasNormals: normals != null, hasUVs: generateUV);

            int[,] pixelToVert = new int[points.Height, points.Width];
            for(int r = 0; r < points.Height; r++)
            {
                for(int c = 0; c < points.Width; c++)
                {
                    pixelToVert[r, c] = -1;
                }
            }

            int getOrAddVert(int r, int c)
            {
                if (pixelToVert[r,c] == -1)
                {
                    pixelToVert[r,c] = ret.Vertices.Count;
                    Vertex v = new Vertex();
                    v.Position = new Vector3(points[0, r, c], points[1, r, c], points[2, r, c]);
                    if (normals != null)
                    {
                        v.Normal = new Vector3(normals[0, r, c], normals[1, r, c], normals[2, r, c]);
                    }
                    if (generateUV)
                    {
                        v.UV = points.PixelToUV(new Vector2(c, r));  // TODO: PixelToUV should handle half pixel offset
                    }
                    ret.Vertices.Add(v);
                }
                return pixelToVert[r,c];
            }

            bool isFaceValid(TriFace f)
            {
                if (!points.IsValid(f.r0, f.c0) || !points.IsValid(f.r1, f.c1) || !points.IsValid(f.r2, f.c2))
                {
                    return false;
                }
                if (normals != null &&
                    (!normals.IsValid(f.r0, f.c0) || !normals.IsValid(f.r1, f.c1) || !normals.IsValid(f.r2, f.c2)))
                {
                    return false;
                }
                if (mask != null && (mask[0, f.r0, f.c0] == 0 || mask[0, f.r1, f.c1] == 0 || mask[0, f.r2, f.c2] == 0))
                {
                    return false;
                }
                return true;
            }

            bool addFaceMaybe(TriFace f)
            {
                if (!isFaceValid(f))
                {
                    return false;
                }
                addFace(f);
                return true;
            }

            void addFace(TriFace f)
            {
                Vector3 v0 = new Vector3(points[0, f.r0, f.c0], points[1, f.r0, f.c0], points[2, f.r0, f.c0]);
                Vector3 v1 = new Vector3(points[0, f.r1, f.c1], points[1, f.r1, f.c1], points[2, f.r1, f.c1]);
                Vector3 v2 = new Vector3(points[0, f.r2, f.c2], points[1, f.r2, f.c2], points[2, f.r2, f.c2]);

                double s0 = Vector3.Distance(v0, v1);
                double s1 = Vector3.Distance(v1, v2);
                double s2 = Vector3.Distance(v2, v0);

                double l = Math.Min(s0, Math.Min(s1, s2));
                double u = Math.Max(s0, Math.Max(s1, s2));
                if (l > 0 && u / l <= maxTriangleAspect)
                {
                    int a = getOrAddVert(f.r0, f.c0);
                    int b = getOrAddVert(f.r1, f.c1);
                    int c = getOrAddVert(f.r2, f.c2);
                    ret.Faces.Add(reverseWinding ? new Face(a, c, b) : new Face(a, b, c));
                }
            };

            int ctrRow = points.Height / 2, ctrCol = points.Width / 2;

            List<int> tris = new List<int>();
            for (int r = 0; r < points.Height - 1; r++)
            {
                for (int c = 0; c < points.Width - 1; c++)
                {
                    //    (r, c)-----(r, c + 1)
                    //         |\    |       
                    //         | \ B |        
                    //         |  \  |         
                    //         | A \ |          
                    //         |    \|           
                    //(r + 1, c)-----(r + 1, c + 1)

                    //    (r, c)-----(r, c + 1)
                    //         |    /|       
                    //         | C / |        
                    //         |  /  |         
                    //         | / D |          
                    //         |/    |           
                    //(r + 1, c)-----(r + 1, c + 1)

                    TriFace fa = new TriFace(r, c, r + 1, c, r + 1, c + 1);
                    TriFace fb = new TriFace(r, c, r + 1, c + 1, r, c + 1);
                    TriFace fc = new TriFace(r, c, r + 1, c, r, c + 1);
                    TriFace fd = new TriFace(r, c + 1, r + 1, c, r + 1, c + 1);

                    //if all four corners are valid points then in most cases it doesn't matter
                    //whether we triangulate the quad as AB or CD
                    //however when we heightmap atlas the peripheral orbital mesh
                    //and then warp its texture coordinates radially
                    //there can be artifacts near the global mesh diagonal opposite the local triangle diagonals
                    //unless we prefer AB in the upper left and lower right quadrants
                    //and CD in the lower left and upper right quadrants
                    bool preferAB = (r < ctrRow && c < ctrCol) || (r >= ctrRow && c >= ctrCol);

                    if (quadsOnly)
                    {
                        if (preferAB)
                        {
                            if (isFaceValid(fa) && isFaceValid(fb))
                            {
                                addFace(fa);
                                addFace(fb);
                            }
                        }
                        else if (isFaceValid(fc) && isFaceValid(fd))
                        {
                            addFace(fc);
                            addFace(fd);
                        }
                    }
                    else if (preferAB)
                    {
                        if (addFaceMaybe(fa))
                        {
                            addFaceMaybe(fb);
                        }
                        else if (addFaceMaybe(fc))
                        {
                            addFaceMaybe(fd);
                        }
                        else if (!addFaceMaybe(fb))
                        {
                            addFaceMaybe(fd);
                        }
                    }
                    else
                    {
                        if (addFaceMaybe(fc))
                        {
                            addFaceMaybe(fd);
                        }
                        else if (addFaceMaybe(fa))
                        {
                            addFaceMaybe(fb);
                        }
                        else if (!addFaceMaybe(fd))
                        {
                            addFaceMaybe(fb);
                        }
                    }
                }
            }

            //if we're going to generate normals, do it here before we add any isolated point cubes
            //because otherwise the cube normals will get munged
            //though actually that might look OK too
            if (generateNormals && !ret.HasNormals)
            {
                ret.GenerateVertexNormals();

                if (flipGeneratedNormalsToward.HasValue)
                {
                    ret.FlipNormalsTowardPoint(flipGeneratedNormalsToward.Value);
                }
            }

            if (isolatedPointSize > 0)
            {
                List<Mesh> cubes = new List<Mesh>();
                for (int row = 0; row < points.Height; row++)
                {
                    for (int col = 0; col < points.Width; col++)
                    {
                        if (points.IsValid(row, col) && pixelToVert[row, col] == -1 &&
                            (mask == null || mask[0, row, col] != 0))
                        {
                            var cube = BoundingBoxExtensions.MakeCube(isolatedPointSize).ToMesh();
                            cube.Transform(Matrix.CreateTranslation(points[0, row, col],
                                                                    points[1, row, col],
                                                                    points[2, row, col]));
                            var uv = new Vector2(((double)row)/points.Width, ((double)col)/points.Height);
                            foreach (var vert in cube.Vertices)
                            {
                                vert.UV = uv;
                            }
                            cubes.Add(cube);
                        }
                    }
                }
                //the cubes have normals but it's OK here if ret does not
                ret.MergeWith(cubes.ToArray());
            }

            return ret;
        }
    }
}
