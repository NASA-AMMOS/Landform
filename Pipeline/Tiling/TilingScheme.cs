using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using JPLOPS.Geometry;

namespace JPLOPS.Pipeline
{
    public enum TilingScheme {
        Bin, QuadX, QuadY, QuadZ, QuadAuto, QuadAutoWithFallback, Oct, UserDefined, Flat, Progressive
    } 

    public abstract class TilingSchemeBase
    {
        protected readonly double minExtent;

        public static bool IsUserProvided(TilingScheme scheme)
        {
            return scheme == TilingScheme.UserDefined || scheme == TilingScheme.Flat;
        }

        public TilingSchemeBase(double minExtent)
        {
            this.minExtent = minExtent;
        }

        public static TilingSchemeBase Create(TilingScheme scheme, double minExtent)
        {
            switch (scheme)
            {
                case TilingScheme.Bin: return new BinaryTreeTilingScheme(minExtent);
                case TilingScheme.QuadX: return new QuadTreeTilingScheme(minExtent, BoxAxis.X);
                case TilingScheme.QuadY: return new QuadTreeTilingScheme(minExtent, BoxAxis.Y);
                case TilingScheme.QuadZ: return new QuadTreeTilingScheme(minExtent, BoxAxis.Z);
                case TilingScheme.QuadAuto: return new QuadTreeTilingScheme(minExtent);
                case TilingScheme.QuadAutoWithFallback: return new QuadTreeTilingScheme(minExtent, true);
                case TilingScheme.Oct: return new OctreeTilingScheme(minExtent);
                default: throw new ArgumentException("unsupported tiling scheme: " + scheme);
            }
        }

        /// <summary>
        /// subdivide a node bounding box into a set of bounding boxes of its children
        /// </summary>
        public abstract IEnumerable<BoundingBox> Split(BoundingBox box);
    }

    public class BinaryTreeTilingScheme : TilingSchemeBase
    {
        public BinaryTreeTilingScheme(double minExtent) : base(minExtent) { }
    
        public override IEnumerable<BoundingBox> Split(BoundingBox box)
        {
            var axis = box.MaxAxis(out double maxDim);
            if (minExtent > 0 && maxDim < 2 * minExtent)
            {
                return new List<BoundingBox>();
            }
            return box.Halves(axis);
        }
    }

    public class QuadTreeTilingScheme : TilingSchemeBase
    {
        private readonly bool auto;
        private readonly BoxAxis fixedAxis;
        private readonly TilingSchemeBase fallback;

        public QuadTreeTilingScheme(double minExtent, BoxAxis fixedAxis, bool withFallback = false) : base(minExtent)
        {
            auto = false;
            this.fixedAxis = fixedAxis;
            fallback = withFallback ? new BinaryTreeTilingScheme(minExtent) : null;
        }

        /// <summary>
        /// Fallback to binary tree may not be desirable because binary tree splitting will generally make rectangular
        /// tiles, but often we make square texture images.  Because UV atlassing often minimizes texture stretch, and
        /// because surface geometry is often quasi-planar, rectangular tiles combined with square texture images often
        /// result in poor texture utilization.
        /// </summary>
        public QuadTreeTilingScheme(double minExtent, bool withFallback = false) : base(minExtent)
        {
            auto = true;
            fixedAxis = BoxAxis.Z;
            fallback = withFallback ? new BinaryTreeTilingScheme(minExtent) : null;
        }

        public override IEnumerable<BoundingBox> Split(BoundingBox box)
        {
            //box face with max area is perpendicular to min axis
            var axis = auto ? box.MinAxis() : fixedAxis;
            if (minExtent > 0)
            {
                Vector2 faceSize = box.GetFaceSizePerpendicularToAxis(axis);
                double minDim = Math.Min(faceSize.X, faceSize.Y);
                if (minDim < 2 * minExtent)
                {
                    if (fallback != null)
                    {
                        return fallback.Split(box);
                    }
                    else
                    {
                        return new BoundingBox[] { box };
                    }
                }
            }
            return box.Quarters(axis);
        }
    }

    public class OctreeTilingScheme : TilingSchemeBase
    {
        private readonly TilingSchemeBase fallback;

        public OctreeTilingScheme(double minExtent) : base(minExtent)
        {
            fallback = QuadTreeTilingScheme.Create(TilingScheme.QuadAuto, minExtent);
        }

        public override IEnumerable<BoundingBox> Split(BoundingBox box)
        {
            if (minExtent > 0 && box.MinDimension() < 2 * minExtent)
            {
                return fallback.Split(box);
            }
            return box.Octants();
        }
    }
}
