using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Alignment
{
    public class KDNode
    {
        public Vector2 Location;
        public KDNode Left, Right;

        public KDNode(Vector2 location, KDNode leftChild, KDNode rightChild)
        {
            Location = location;
            Left = leftChild;
            Right = rightChild;
        }

        public override string ToString()
        {
            return string.Format("{0}: \n\t{1}\n\t{2}", Location.ToString(), Left.ToString(), Right.ToString());
        }

        public KDNode KDTree(IEnumerable<ImageFeature> features, int depth = 0)
        {
            int k = 2;
            int axis = depth % k;
            int median = features.Count();

            if (axis == 0) {
                features = features.OrderBy(x => x.Location.X);
            }
            else if (axis == 1)
            {
                features = features.OrderBy(x => x.Location.Y);
            }

            return new KDNode(features.ElementAt(median).Location, 
                              KDTree(features.Take(median), depth + 1),
                              KDTree(features.Skip(median + 1).Take(features.Count() - (median + 1)), depth + 1));
        }
    }
}
