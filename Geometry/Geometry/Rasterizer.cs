using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Diagnostics;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using OPS.MathExtensions;
using OPS.Util;
using OPS.Imaging;

namespace OPS.Geometry
{
    public enum BlendMode { Over, Under, Average, Max, Min };

    public class Rasterizer
    {
        public class Options
        {
            public double MetersPerPixel = 0.005;

            public double MaxRadiusMeters = 20; //clamp mesh bounds in image plane to this limit if positive

            public int WidthPixels = 0; //if non-positive compute from mesh bounds, MetersPerPixel, and MaxRadiusMeters
            public int HeightPixels = 0; //if non-positive compute from mesh bounds, MetersPerPixel, and MaxRadiusMeters

            public Vector3 CameraLocation = Vector3.Zero;
            public Vector3 RightInImage = new Vector3(1, 0, 0);
            public Vector3 DownInImage = new Vector3(0, 1, 0);

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
            public Func<Mesh, Face, bool> FaceFilter = null; //true = rasterize face

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
                    MaxRadiusMeters = 0,
                        
                    //mesh coordinates are already in image pixel space
                    MetersPerPixel = 1,
                    WidthPixels = img.Width,
                    HeightPixels = img.Height,
                        
                    ImageFactory = (b, w, h) => img, //rasterize into supplied image

                    MaskFactory = (w, h) => new Image(1, w, h) //otherwise MaskFactory would default to ImageFactory
                };
            }
        }

        /// <summary>
        /// rasterize a mesh using a parallel projection camera
        ///
        /// camera extrinsics (pose) and intrinsics (resolution) are controlled by options
        ///
        /// if mesh has UVs and img is not null it will be texture mapped, otherwise vertex colors will be used
        ///
        /// backface culling and z buffering are TODO https://github.jpl.nasa.gov/OnSight/Landform/issues/733
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
                return new Vector3(Vector3.Dot(camToPt, right) * pixelsPerMeter + ctrPixel.X,
                                   Vector3.Dot(camToPt, down) * pixelsPerMeter + ctrPixel.Y,
                                   Vector3.Dot(camToPt, forward));
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

            double relDist(Vector2 p, Vector2 a, Vector2 b)
            {
                var n = new Vector2(a.Y - b.Y, b.X - a.X); //normal to segment from a to b
                return p.Dot(n) - a.Dot(n);
            }

            Action<int, int, int, float, bool> blend = null;
            switch (options.BlendMode)
            {
                case BlendMode.Over:
                {
                    blend = (int b, int r, int c, float v, bool overdraw) => { ret[b, r, c] = v; };
                    break;
                }
                case BlendMode.Under:
                {
                    blend = (int b, int r, int c, float v, bool overdraw) =>
                        {
                            if (!overdraw)
                            {
                                ret[b, r, c] = v;
                            }
                        };
                    break;
                }
                case BlendMode.Average:
                {
                    blend = (int b, int r, int c, float v, bool overdraw) =>
                        {
                            ret[b, r, c] = overdraw ? 0.5f * (ret[b, r, c] + v) : v;
                        };
                    break;
                }
                case BlendMode.Max:
                {
                    blend = (int b, int r, int c, float v, bool overdraw) =>
                        {
                            ret[b, r, c] = overdraw ? Math.Max(ret[b, r, c], v) : v;
                        };
                    break;
                }
                case BlendMode.Min:
                {
                    blend = (int b, int r, int c, float v, bool overdraw) =>
                        {
                            ret[b, r, c] = overdraw ? Math.Min(ret[b, r, c], v) : v;
                        };
                    break;
                }
            }

            Vector2 zero = new Vector2(0, 0), one = new Vector2(1, 1);
            void writeFragment(int r, int c, Vertex v0, Vertex v1, Vertex v2, double d0, double d1, double d2,
                               double alpha, double beta, double gamma)
            {
                //TODO z buffer
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
            }

            Func<Mesh, Face, bool> filter = options.FaceFilter ?? ((m, t) => true);

            foreach (var t in mesh.Faces)
            {
                if (!filter(mesh, t))
                {
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
                    alpha = beta = gamma = 1.0 / 3;
                    for (int r =  minR; r <= maxR; r++)
                    {
                        for (int c = minC; c <= maxC; c++)
                        { 
                            writeFragment(r, c, v0, v1, v2, d0, d1, d2, alpha, beta, gamma);
                        }
                    }
                }
                else
                {
                    //TODO backface cull 
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
                                writeFragment(r, c, v0, v1, v2, d0, d1, d2, alpha, beta, gamma);
                            }
                        }
                    }
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
                Func<int, int, Image> maskFactory = options.MaskFactory;
                if (maskFactory == null)
                {
                    maskFactory = (w, h) => imageFactory(1, w, h);
                }
                //inpaint just the interior holes
                //we do this by first creating a mask by floodfilling exterior invalid regions
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
