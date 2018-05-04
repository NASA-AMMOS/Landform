using OPS.Imaging;
using OPS.Plumbing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Alignment
{
    /// <summary>
    /// An unordered pair of <see cref="ImageRef"/>s.
    /// </summary>
    public class UnorderedImagePair
    {
        public ImageRef One, Two;

        /// <summary>
        /// Construct from two <see cref="ImageRef"/>s.
        /// 
        /// Note that <code>new UnorderedImagePair(x, y) == new UnorderedImagePair(y, x)</code>.
        /// </summary>
        /// <param name="model"></param>
        /// <param name="data"></param>
        public UnorderedImagePair(ImageRef model, ImageRef data)
        {
            if (data.DisplayName.CompareTo(model.DisplayName) < 0)
            {
                One = data;
                Two = model;
            }
            else
            {
                One = model;
                Two = data;
            }
        }
        public UnorderedImagePair()
        {
            One = null;
            Two = null;
        }

        public override int GetHashCode()
        {
            return One.GetHashCode() ^ Two.GetHashCode();
        }

        public static bool operator ==(UnorderedImagePair lhs, UnorderedImagePair rhs)
        {
            // one & two are sorted so there is no order dependency
            return lhs.One == rhs.One && lhs.Two == rhs.Two;
        }
        public static bool operator !=(UnorderedImagePair lhs, UnorderedImagePair rhs)
        {
            // see ==
            return lhs.One != rhs.One || lhs.Two != rhs.Two;
        }

        public override bool Equals(object obj)
        {
            if (obj == null || !(obj is UnorderedImagePair)) {
                return false;
            }

            UnorderedImagePair asPair = (UnorderedImagePair)obj;
            return this == asPair;
        }
    }
}
