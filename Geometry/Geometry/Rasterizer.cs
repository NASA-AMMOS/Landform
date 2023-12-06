using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using JPLOPS.MathExtensions;
using JPLOPS.Imaging;

namespace JPLOPS.Geometry
{
    public class Rasterizer
    {
        public enum CullMode { None, KeepCCWFaces, KeepCWFaces }
        public enum DepthTest { None, KeepCloser, KeepCloserOrEqual, KeepFurther, KeepFurtherOrEqual }
        public enum BlendMode { Over, Under, Average, Max, Min };

        public class Options
        {
            public double MetersPerPixel = 0.005;

            public double MaxRadiusMeters = 20; //clamp mesh bounds in image plane to this limit if positive

            public int WidthPixels = 0; //if non-positive compute from mesh bounds, MetersPerPixel, and MaxRadiusMeters
            public int HeightPixels = 0; //if non-positive compute from mesh bounds, MetersPerPixel, and MaxRadiusMeters

            public Vector3 CameraLocation = Vector3.Zero;
            public Vector3 RightInImage = new Vector3(1, 0, 0);
            public Vector3 DownInImage = new Vector3(0, 1, 0);

            public CullMode CullMode = CullMode.KeepCCWFaces;
            public DepthTest DepthTest = DepthTest.KeepCloser;
            public BlendMode BlendMode = BlendMode.Average;

            public bool Greyscale = false;

            public double SparseBlockSize = 0.005;
            public double MinSparseBlockValidRatio = 0.8;
            public double KeepLargestComponents = 0.2; //keep components within this tol of size of largest, 0 disables

            public int Inpaint = 20;
            public int Blur = 0;
            public int Decimate = 2;

            [JsonIgnore]
            public Func<int, int, int, Image> ImageFactory = null; //defaults to new Image()

            [JsonIgnore]
            public Func<int, int, Image> MaskFactory = null; //defaults to use ImageFactory

            [JsonIgnore]
            public Func<int, int, Image> DepthBufferFactory = null; //defaults to use ImageFactory

            [JsonIgnore]
            public Func<Mesh, Face, bool> FaceFilter = null; //true = rasterize face

            [JsonIgnore]
            public bool ComputeAreaStats = false;

            [JsonIgnore]
            public Action<Stats> StatsCallback = null; //stats are computed before sparse block removal and decimation

            [JsonIgnore]
            public Func<Vector2, Vector2> Warp = null; //applied to projected points

            public Options Clone()
            {
                return (Options) MemberwiseClone();
            }

            public static Options DirectToImage(Image img)
            {
                return new Options() {

                    BlendMode = BlendMode.Under, //don't overwrite any already valid pixels
                        
                    Greyscale = img.Bands == 1,
                        
                    //disable extra stuff
                    SparseBlockSize = 0,
                    Inpaint = 0,
                    Blur = 0,
                    Decimate = 0,
                        
                    //mesh coordinates are already in image pixel space
                    MetersPerPixel = 1,
                    MaxRadiusMeters = 0,
                    WidthPixels = img.Width,
                    HeightPixels = img.Height,
                        
                    ImageFactory = (b, w, h) => img, //rasterize into supplied image

                    MaskFactory = (w, h) => new Image(1, w, h), //otherwise would default to ImageFactory
                    DepthBufferFactory = (w, h) => new Image(1, w, h), //otherwise would default to ImageFactory
                };
            }
        }

        
        public class Stats
        {
            public int FilteredTriangles, DegenerateTriangles, CulledTriangles;
            public int DrawnFragments, OverdrawnFragments, OccludedFragments;
            public int MaxTriangleFragments = -1;
            public int MinTriangleFragments = -1;
            public double MaxTriangleArea = -1;
            public double MinTriangleArea = -1;
            public double MaxFragmentsPerSquareMeter = -1;
            public double MinFragmentsPerSquareMeter = -1;
        }

        /// <summary>
        /// rasterize a mesh using a parallel projection camera
        ///
        /// camera extrinsics (pose) and intrinsics (resolution) are controlled by options
        ///
        /// if mesh has UVs and img is not null it will be texture mapped, otherwise vertex colors will be used
        ///
        /// output meshOrigin is the pixel corresponding to the origin of mesh frame (may be outside returned image)
        /// </summary>
        public static Image Rasterize(Mesh mesh, Image img, out Vector2 meshOrigin, Options options = null)
        {
            if (options == null)
            {
                options = new Options();
            }

            Func<int, int, int, Image> imageFactory = options.ImageFactory;
            if (imageFactory == null)
            {
                imageFactory = (b, w, h) => new Image(b, w, h);
            }

            double pixelsPerMeter = 1 / options.MetersPerPixel;

            var right = options.RightInImage;
            var down = options.DownInImage;
            var forward = Vector3.Cross(right, down);

            //may be non-positive if auto-computing
            //also may need to adjust for options.MaxRadiusMeters
            int widthPixels = options.WidthPixels;
            int heightPixels = options.HeightPixels;

            //will be computed below after resolving actual width and height
            Vector2 ctrPixel = Vector2.Zero;

            Vector3 project(Vector3 pt)
            {
                var camToPt = pt - options.CameraLocation;
                pt = new Vector3(Vector3.Dot(camToPt, right) * pixelsPerMeter + ctrPixel.X,
                                 Vector3.Dot(camToPt, down) * pixelsPerMeter + ctrPixel.Y,
                                 Vector3.Dot(camToPt, forward));
                if (options.Warp != null)
                {
                    var warped = options.Warp(pt.XY());
                    pt.X = warped.X;
                    pt.Y = warped.Y;
                }
                return pt;
            }

            if (widthPixels <= 0 || heightPixels <= 0)
            {
                if (mesh.Vertices.Count > 0)
                {
                    var min = new Vector2(double.PositiveInfinity, double.PositiveInfinity);
                    var max = new Vector2(double.NegativeInfinity, double.NegativeInfinity);
                    foreach (var v in mesh.Vertices)
                    {
                        var px = project(v.Position);
                        min.X = Math.Min(px.X, min.X);
                        min.Y = Math.Min(px.Y, min.Y);
                        max.X = Math.Max(px.X, max.X);
                        max.Y = Math.Max(px.Y, max.Y);
                    }
                    widthPixels = Math.Max(widthPixels, (int)Math.Ceiling(max.X - min.X));
                    heightPixels = Math.Max(heightPixels, (int)Math.Ceiling(max.Y - min.Y));
                }
                widthPixels = Math.Max(widthPixels, 1);
                heightPixels = Math.Max(heightPixels, 1);
            }

            if (options.MaxRadiusMeters > 0)
            {
                int maxDiameterPixels = (int)Math.Ceiling(2 * options.MaxRadiusMeters * pixelsPerMeter);
                if (widthPixels > maxDiameterPixels)
                {
                    widthPixels = maxDiameterPixels;
                    ctrPixel.X = 0.5 * widthPixels;
                }
                if (heightPixels > maxDiameterPixels)
                {
                    heightPixels = maxDiameterPixels;
                    ctrPixel.Y = 0.5 * heightPixels;
                }
            }

            ctrPixel = new Vector2(widthPixels, heightPixels) * 0.5;

            bool greyscale = options.Greyscale || (img != null && img.Bands == 1);
            int bands = greyscale ? 1 : 3;
            if (mesh.HasUVs && img != null && img.Bands != bands)
            {
                throw new ArgumentException(string.Format("got {0} band texture, expected {1}", img.Bands, bands));
            } 

            var ret = imageFactory(bands, widthPixels, heightPixels);

            if (!ret.HasMask) //respect any existing mask
            {
                ret.CreateMask(true); //pixels default to masked
            }

            Action<int, int, int, float, bool> blend = null;
            switch (options.BlendMode)
            {
                case BlendMode.Over: blend = (b, r, c, v, overdraw) => { ret[b, r, c] = v; }; break;
                case BlendMode.Under: blend = (b, r, c, v, overdraw) => { if (!overdraw) { ret[b, r, c] = v; } }; break;
                case BlendMode.Average:
                {
                    blend = (b, r, c, v, overdraw) => { ret[b, r, c] = overdraw ? 0.5f * (ret[b, r, c] + v) : v; };
                    break;
                }
                case BlendMode.Max:
                {
                    blend = (b, r, c, v, overdraw) => { ret[b, r, c] = overdraw ? Math.Max(ret[b, r, c], v) : v; };
                    break;
                }
                case BlendMode.Min:
                {
                    blend = (b, r, c, v, overdraw) => { ret[b, r, c] = overdraw ? Math.Min(ret[b, r, c], v) : v; };
                    break;
                }
            }

            Vector2 zero = new Vector2(0, 0), one = new Vector2(1, 1);
            bool writeFragment(int r, int c, Vertex v0, Vertex v1, Vertex v2, double alpha, double beta, double gamma)
            {
                bool overdraw = ret.IsValid(r, c);
                if (mesh.HasUVs && img != null)
                {
                    var src = img.UVToPixel(Vector2.Clamp(v0.UV * alpha + v1.UV * beta + v2.UV * gamma, zero, one));
                    int sr = MathE.Clamp((int)src.Y, 0, img.Height - 1);
                    int sc = MathE.Clamp((int)src.X, 0, img.Width - 1);
                    if (img.IsValid(sr, sc))
                    {
                        blend(0, r, c, img[0, sr, sc], overdraw);
                        if (bands == 3)
                        {
                            blend(1, r, c, img[1, sr, sc], overdraw);
                            blend(2, r, c, img[2, sr, sc], overdraw);
                        }
                        ret.SetMaskValue(r, c, false);
                    }
                }
                else
                {
                    blend(0, r, c, (float)(v0.Color.X * alpha + v1.Color.X * beta + v2.Color.X * gamma), overdraw);
                    if (bands == 3)
                    {
                        blend(1, r, c, (float)(v0.Color.Y * alpha + v1.Color.Y * beta + v2.Color.Y * gamma), overdraw);
                        blend(2, r, c, (float)(v0.Color.Z * alpha + v1.Color.Z * beta + v2.Color.Z * gamma), overdraw);
                    }
                    ret.SetMaskValue(r, c, false);
                }
                return overdraw;
            }

            Func<Vector2, Vector2, double> crossZ = (a, b) => a.X * b.Y - a.Y * b.X;
            Func<Vector2, Vector2, Vector2, bool> cull = null;
            switch (options.CullMode)
            {
                //careful: because image is X right, Y down handedness is flipped in pixel space
                case CullMode.KeepCCWFaces: cull = (p0, p1, p2) => crossZ(p1 - p0, p2 - p0) > 0; break;
                case CullMode.KeepCWFaces: cull = (p0, p1, p2) => crossZ(p1 - p0, p2 - p0) < 0; break;
                default: cull = (p0, p1, p2) => false; break;
            }

            Image depthBuffer = null;
            if (options.DepthTest != DepthTest.None)
            {
                var depthBufferFactory = options.DepthBufferFactory ?? ((w, h) => imageFactory(1, w, h));

                depthBuffer = depthBufferFactory(ret.Width, ret.Height);

                if (options.DepthTest == DepthTest.KeepFurther || options.DepthTest == DepthTest.KeepFurtherOrEqual)
                {
                    depthBuffer.Fill(new float[] { float.NegativeInfinity });
                }
                else
                {
                    depthBuffer.Fill(new float[] { float.PositiveInfinity });
                }
            }

            Func<int, int, double, double, double, double, double, double, bool>
                depthTest(Func<double, double, bool> shouldWrite)
            {
                return (int r, int c, double d0, double d1, double d2, double alpha, double beta, double gamma) =>
                {
                    double fragmentDepth = d0 * alpha + d1 * beta + d2 * gamma;
                    if (shouldWrite(fragmentDepth, depthBuffer[0, r, c]))
                    {
                        depthBuffer[0, r, c] = (float)fragmentDepth;
                        return true;
                    }
                    return false;
                };
            }

            Func<int, int, double, double, double, double, double, double, bool> depthTestFragment = null;
            switch (options.DepthTest)
            {
                case DepthTest.KeepCloser:         depthTestFragment = depthTest((fd, db) => fd < db); break;
                case DepthTest.KeepCloserOrEqual:  depthTestFragment = depthTest((fd, db) => fd <= db); break;
                case DepthTest.KeepFurther:        depthTestFragment = depthTest((fd, db) => fd > db); break;
                case DepthTest.KeepFurtherOrEqual: depthTestFragment = depthTest((fd, db) => fd >= db); break;
                default: depthTestFragment = (r, c, d0, d1, d2, alpha, beta, gamma) => true; break;
            }

            Func<Mesh, Face, bool> filter = options.FaceFilter ?? ((m, t) => true);

            double relDist(Vector2 p, Vector2 a, Vector2 b)
            {
                var n = new Vector2(a.Y - b.Y, b.X - a.X); //normal to segment from a to b
                return p.Dot(n) - a.Dot(n);
            }

            var stats = new Stats();

            foreach (var t in mesh.Faces)
            {
                if (!filter(mesh, t))
                {
                    stats.FilteredTriangles++;
                    continue;
                }

                var v0 = mesh.Vertices[t.P0];
                var v1 = mesh.Vertices[t.P1];
                var v2 = mesh.Vertices[t.P2];

                var pd0 = project(v0.Position);
                var pd1 = project(v1.Position);
                var pd2 = project(v2.Position);

                var p0 = new Vector2(pd0.X, pd0.Y);
                var p1 = new Vector2(pd1.X, pd1.Y);
                var p2 = new Vector2(pd2.X, pd2.Y);

                double d0 = pd0.Z;
                double d1 = pd1.Z;
                double d2 = pd2.Z;

                var minR = (int)Math.Max(0, Math.Min(Math.Min(p0.Y, p1.Y), p2.Y));
                var maxR = (int)Math.Min(ret.Height - 1, Math.Max(Math.Max(p0.Y, p1.Y), p2.Y));

                var minC = (int)Math.Max(0, Math.Min(Math.Min(p0.X, p1.X), p2.X));
                var maxC = (int)Math.Min(ret.Width - 1, Math.Max(Math.Max(p0.X, p1.X), p2.X));

                //if tri is entirely outside raster at this point we'll have either
                //minR > maxR or minC > maxC

                double alpha, beta, gamma;
                if (minR == maxR || minC == maxC) //degenerate
                {
                    stats.DegenerateTriangles++;
                    alpha = beta = gamma = 1.0 / 3;
                    for (int r =  minR; r <= maxR; r++)
                    {
                        for (int c = minC; c <= maxC; c++)
                        { 
                            if (depthTestFragment(r, c, d0, d1, d2, alpha, beta, gamma))
                            {
                                bool overdraw = writeFragment(r, c, v0, v1, v2, alpha, beta, gamma);
                                if (overdraw)
                                {
                                    stats.OverdrawnFragments++;
                                }
                                else
                                {
                                    stats.DrawnFragments++;
                                }
                            }
                            else
                            {
                                stats.OccludedFragments++;
                            }
                        }
                    }
                }
                else if (!cull(p0, p1, p2))
                {
                    int nf = 0;
                    for (int r =  minR; r <= maxR; r++)
                    {
                        for (int c = minC; c <= maxC; c++)
                        { 
                            var px = new Vector2(c, r);
                            alpha = relDist(px, p1, p2) / relDist(p0, p1, p2);
                            beta  = relDist(px, p2, p0) / relDist(p1, p2, p0);
                            gamma = relDist(px, p0, p1) / relDist(p2, p0, p1);
                            if ((alpha >= 0) && (beta >= 0) && (gamma >= 0))
                            {
                                nf++;
                                if (depthTestFragment(r, c, d0, d1, d2, alpha, beta, gamma))
                                {
                                    bool overdraw = writeFragment(r, c, v0, v1, v2, alpha, beta, gamma);
                                    if (overdraw)
                                    {
                                        stats.OverdrawnFragments++;
                                    }
                                    else
                                    {
                                        stats.DrawnFragments++;
                                    }
                                }
                                else
                                {
                                    stats.OccludedFragments++;
                                }
                            }
                        }
                    }
                    if (nf > stats.MaxTriangleFragments || stats.MaxTriangleFragments < 0)
                    {
                        stats.MaxTriangleFragments = nf;
                    }
                    if (nf < stats.MinTriangleFragments || stats.MinTriangleFragments < 0)
                    {
                        stats.MinTriangleFragments = nf;
                    }
                    if (options.ComputeAreaStats)
                    {
                        var area = new Triangle(v0, v1, v2).Area();
                        if (area > stats.MaxTriangleArea || stats.MaxTriangleArea < 0)
                        {
                            stats.MaxTriangleArea = area;
                        }
                        if (area < stats.MinTriangleArea || stats.MinTriangleArea < 0)
                        {
                            stats.MinTriangleArea = area;
                        }
                        var fpm = nf / area;
                        if (fpm > stats.MaxFragmentsPerSquareMeter || stats.MaxFragmentsPerSquareMeter < 0)
                        {
                            stats.MaxFragmentsPerSquareMeter = fpm;
                        }
                        if (fpm < stats.MinFragmentsPerSquareMeter || stats.MinFragmentsPerSquareMeter < 0)
                        {
                            stats.MinFragmentsPerSquareMeter = fpm;
                        }
                    }
                }
                else
                {
                    stats.CulledTriangles++;
                }
            }

            meshOrigin = project(Vector3.Zero).XY();

            if (options.SparseBlockSize > 0)
            {
                int sbs = options.SparseBlockSize < 1 ?
                    (int)(options.SparseBlockSize * Math.Max(ret.Width, ret.Height)) :
                    (int)options.SparseBlockSize;
                sbs = Math.Max(sbs, 1);
                ret.InvalidateSparseExternalBlocks(sbs, options.MinSparseBlockValidRatio);
                if (options.KeepLargestComponents > 0)
                {
                    ret.InvalidateAllButLargestValidBlobs(options.KeepLargestComponents);
                }
                ret = ret.Trim(out Vector2 ulc);
                meshOrigin -= ulc;
            }

            if (options.Inpaint > 0)
            {
                //inpaint just the interior holes
                //we do this by first creating a mask by floodfilling exterior invalid regions
                var maskFactory = options.MaskFactory ?? ((w, h) => imageFactory(1, w, h));
                Image mask = maskFactory(ret.Width, ret.Height);
                ret.AddOuterRegionsToMask(mask);
                ret.Inpaint(options.Inpaint);
                ret.UnionMask(mask, new float[] { 1 } ); //re-apply the exterior mask
            }

            //can't use Image.Resize() here because it doesn't preserve mask
            //but Image.Decimated() does

            if (options.Blur > 0)
            {
                ret.GaussianBoxBlur(options.Blur);
            }

            if (options.Decimate > 1)
            {
                ret = ret.Decimated(options.Decimate);
                meshOrigin /= options.Decimate;
            }

            if (options.StatsCallback != null)
            {
                options.StatsCallback(stats);
            }
                    
            return ret;
        }

        public static Image Rasterize(Mesh mesh, Image img, Options options = null)
        {
            return Rasterize(mesh, img, out Vector2 meshOrigin, options);
        }

        /// <summary>
        /// Delaunay triangulate non-masked pixels, then barycentric interpolate pixel colors in their convex hull.
        /// Supplied image must have 1 or 3 bands and a mask.
        /// </summary>
        public static Image BarycentricInterpolate(Image img, Func<Mesh, Face, bool> filter = null)
        {
            if (img.Bands != 1 && img.Bands != 3)
            {
                throw new ArgumentException("only 1 or 3 band images supported");
            }

            if (!img.HasMask)
            {
                throw new ArgumentException("supplied image must have a mask");
            }

            var seeds = new List<Vertex>();
            for (int r = 0; r < img.Height; r++)
            {
                for (int c = 0; c < img.Width; c++)
                {
                    if (img.IsValid(r, c))
                    {
                        var color = new Vector4();
                        color.X = img[0, r, c];
                        if (img.Bands == 3)
                        {
                            color.Y = img[1, r, c];
                            color.Z = img[2, r, c];
                        }

                        var vert = new Vertex(c, r, 0);
                        vert.Color = color;

                        seeds.Add(vert);
                    }
                }
            }

            if (seeds.Count < 3)
            {
                throw new ArgumentException("supplied image must have at least 3 unmasked pixels");
            }

            var opts = Options.DirectToImage(img);
            if (filter != null)
            {
                opts.FaceFilter = filter;
            }
            return Rasterize(Delaunay.Triangulate(seeds), null, opts);
        }
    }
}
