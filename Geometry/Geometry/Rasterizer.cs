using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Diagnostics;
using log4net;
using Microsoft.Xna.Framework;
using OPS.MathExtensions;
using OPS.Util;
using OPS.Imaging;

namespace OPS.Geometry
{
    public enum BlendMode { Over, Under, Average, Max, Min };

    public class Rasterizer
    {
        public class BEVOptions
        {
            public BlendMode BlendMode = BlendMode.Average;
            public bool CCW = false;
            public double MetersPerPixel = 0.005;
            public bool Greyscale = false;
            public double SparseBlockSize = 0.005;
            public double MinSparseBlockValidRatio = 0.8;
            public int Inpaint = 20;
            public int Blur = 0;
            public int Decimate = 2;
            public double MaxRadiusMeters = 20;
            public bool RadiusRelativeToOrigin = false;
            public int WidthPixels = 0; //if non-positive auto compute based on mesh bounds and MetersPerPixel
            public int HeightPixels = 0; //if non-positive auto compute based on mesh bounds and MetersPerPixel
            public Vector2? MeshOffset = null; //XY plane offset to apply to mesh, auto compute if null
            public Func<int, int, int, Image> ImageFactory = null; //defaults to new Image()
            public Func<int, int, Image> MaskFactory = null; //defaults to use ImageFactory

            public BEVOptions Clone()
            {
                return (BEVOptions) MemberwiseClone();
            }
        }

        /// <summary>
        /// rasterize a birds eye view image of mesh
        ///
        /// if mesh has UVs and img is not null it will be texture mapped
        /// otherwise the mesh vertex colors will be used
        ///
        /// the view is from above but assuming +Z is down, so that we are looking at the backfaces of ccw triangles
        /// and we do render the backfaces
        /// you can flip all that by specifying ccw = true in the options
        ///
        /// occlusion is painters algorithm, so sort the mesh faces if you need to
        ///
        /// input meshOrigin is the center in mesh frame to use if MaxRadiusMeters>0 and RadiusRelativeToOrigin=true
        /// (if MaxRadiusMeters>0 but RadiusRelativeToOrigin=false then the mesh bounds center is used)
        ///
        /// output meshOrigin is the pixel corresponding to the origin of mesh frame (which may be outside image)
        /// </summary>
        public static Image RenderBirdsEyeView(Mesh mesh, Image img, ref Vector2 meshOrigin, BEVOptions options = null)
        {
            if (options == null)
            {
                options = new BEVOptions();
            }

            Func<int, int, int, Image> imageFactory = options.ImageFactory;
            if (imageFactory == null)
            {
                imageFactory = (b, w, h) => new Image(b, w, h);
            }

            bool ccw = options.CCW;
            double pixelsPerMeter = 1 / options.MetersPerPixel;

            var meshBounds = mesh.Bounds();

            if (options.MaxRadiusMeters > 0)
            {
                var ctr = options.RadiusRelativeToOrigin ? options.MetersPerPixel * meshOrigin
                    : 0.5 * new Vector2(meshBounds.Max.X + meshBounds.Min.X, meshBounds.Max.Y + meshBounds.Min.Y);
                if (ctr.X - meshBounds.Min.X > options.MaxRadiusMeters)
                {
                    meshBounds.Min.X = ctr.X - options.MaxRadiusMeters;
                }
                if (meshBounds.Max.X - ctr.X > options.MaxRadiusMeters)
                {
                    meshBounds.Max.X = ctr.X + options.MaxRadiusMeters;
                }
                if (ctr.Y - meshBounds.Min.Y > options.MaxRadiusMeters)
                {
                    meshBounds.Min.Y = ctr.Y - options.MaxRadiusMeters;
                }
                if (meshBounds.Max.Y - ctr.Y > options.MaxRadiusMeters)
                {
                    meshBounds.Max.Y = ctr.Y + options.MaxRadiusMeters;
                }
            }

            int widthPixels = options.WidthPixels;
            if (widthPixels <= 0)
            {
                double widthMeters = meshBounds.Max.X - meshBounds.Min.X;
                widthPixels = (int)(widthMeters * pixelsPerMeter);
            }

            int heightPixels = options.HeightPixels;
            if (heightPixels <= 0)
            {
                double heightMeters = meshBounds.Max.Y - meshBounds.Min.Y;
                heightPixels = (int)(heightMeters * pixelsPerMeter);
            }

            //ptInImageFramePixels = (ptInMeshFrameMeters + offset) * pixelsPerMeter
            Vector2 offset =
                options.MeshOffset.HasValue ? options.MeshOffset.Value :
                -1 * (new Vector2(meshBounds.Min.X, ccw ? meshBounds.Max.Y : meshBounds.Min.Y));

            meshOrigin = offset * pixelsPerMeter;

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
            void writeFragment(int r, int c, Vertex v0, Vertex v1, Vertex v2, double alpha, double beta, double gamma)
            {
                bool overdraw = ret.IsValid(r, c);
                if (mesh.HasUVs && img != null)
                {
                    var src = img.UVToPixel(Vector2.Clamp(v0.UV * alpha + v1.UV * beta + v2.UV * gamma, zero, one));
                    int sr = (int)src.Y, sc = (int)src.X;
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

            foreach (var t in mesh.Faces)
            {
                var v0 = mesh.Vertices[ccw ? t.P0 : t.P2];
                var v1 = mesh.Vertices[t.P1];
                var v2 = mesh.Vertices[ccw ? t.P2 : t.P0];

                if (meshBounds.Contains(v0.Position) == ContainmentType.Disjoint &&
                    meshBounds.Contains(v1.Position) == ContainmentType.Disjoint &&
                    meshBounds.Contains(v2.Position) == ContainmentType.Disjoint)
                {
                    continue;
                }

                var p0 = (new Vector2(v0.Position.X, v0.Position.Y) + offset) * pixelsPerMeter;
                var p1 = (new Vector2(v1.Position.X, v1.Position.Y) + offset) * pixelsPerMeter;
                var p2 = (new Vector2(v2.Position.X, v2.Position.Y) + offset) * pixelsPerMeter;

                var minR = (int)Math.Max(0, Math.Min(Math.Min(p0.Y, p1.Y), p2.Y));
                var maxR = (int)Math.Min(ret.Height - 1, Math.Max(Math.Max(p0.Y, p1.Y), p2.Y));

                var minC = (int)Math.Max(0, Math.Min(Math.Min(p0.X, p1.X), p2.X));
                var maxC = (int)Math.Min(ret.Width - 1, Math.Max(Math.Max(p0.X, p1.X), p2.X));

                double alpha, beta, gamma;
                if (minR == maxR || minC == maxC) //degenerate
                {
                    alpha = beta = gamma = 1.0 / 3;
                    for (int r =  minR; r <= maxR; r++)
                    {
                        for (int c = minC; c <= maxC; c++)
                        { 
                            writeFragment(r, c, v0, v1, v2, alpha, beta, gamma);
                        }
                    }
                }
                else
                {
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
                                writeFragment(r, c, v0, v1, v2, alpha, beta, gamma);
                            }
                        }
                    }
                }
            }

            if (options.SparseBlockSize > 0)
            {
                int sbs = options.SparseBlockSize < 1 ?
                    (int)(options.SparseBlockSize * Math.Max(ret.Width, ret.Height)) :
                    (int)options.SparseBlockSize;
                sbs = Math.Max(sbs, 1);
                ret.InvalidateSparseExternalBlocks(sbs, options.MinSparseBlockValidRatio);
                ret.InvalidateAllButLargestValidBlob();
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

        public static Image RenderBirdsEyeView(Mesh mesh, Image img, BEVOptions options = null)
        {
            Vector2 meshOrigin = new Vector2();
            return RenderBirdsEyeView(mesh, img, ref meshOrigin, options);
        }

        /// <summary>
        /// Delaunay triangulate non-masked pixels, then barycentric interpolate pixel colors in their convex hull.
        /// Supplied image must have 1 or 3 bands and a mask.
        /// </summary>
        public static Image BarycentricInterpolate(Image img)
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

            var options = new BEVOptions() {

                BlendMode = BlendMode.Under, //don't overwrite any already valid pixels

                CCW = true,

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
                MeshOffset = new Vector2(0, 0),

                ImageFactory = (b, w, h) => img //rasterize into supplied image
            };

            return RenderBirdsEyeView(Delaunay.Triangulate(seeds), null, options);
        }
    }
}
