using System;
using System.Linq;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using OPS.Geometry;

namespace OPS.Pipeline
{
    public enum TilingScheme { Bin, QuadX, QuadY, QuadZ, QuadAuto, Oct, UserDefined, Flat } 

    public abstract class TilingSchemeBase
    {
        public static bool IsUserProvided(TilingScheme scheme)
        {
            return scheme == TilingScheme.UserDefined || scheme == TilingScheme.Flat;
        }

        public static TilingSchemeBase Create(TilingScheme scheme)
        {
            switch (scheme)
            {
                case TilingScheme.Bin: return new BinaryTreeTilingScheme();
                case TilingScheme.QuadX: return new QuadTreeTilingScheme(false, BoxAxis.X);
                case TilingScheme.QuadY: return new QuadTreeTilingScheme(false, BoxAxis.Y);
                case TilingScheme.QuadZ: return new QuadTreeTilingScheme(false, BoxAxis.Z);
                case TilingScheme.QuadAuto: return new QuadTreeTilingScheme(true, BoxAxis.Z);
                case TilingScheme.Oct: return new OctreeTilingScheme();
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
        public override IEnumerable<BoundingBox> Split(BoundingBox box)
        {
            return box.Halves(box.MaxAxis());
        }
    }

    public class QuadTreeTilingScheme : TilingSchemeBase
    {
        private readonly bool auto;
        private readonly BoxAxis fixedAxis;

        public QuadTreeTilingScheme(bool auto, BoxAxis fixedAxis)
        {
            this.auto = auto;
            this.fixedAxis = fixedAxis;
        }

        public override IEnumerable<BoundingBox> Split(BoundingBox box)
        {
            //box face with max area is perpendicular to min axis
            return box.Quarters(auto ? box.MinAxis() : fixedAxis);
        }
    }

    public class OctreeTilingScheme : TilingSchemeBase
    {
        public override IEnumerable<BoundingBox> Split(BoundingBox box)
        {
            return box.Octants();
        }
    }
}
