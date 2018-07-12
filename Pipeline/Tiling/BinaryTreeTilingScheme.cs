using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Geometry;
using Microsoft.Xna.Framework;

namespace OPS.Pipeline
{
    public class BinaryTreeTilingScheme : ITilingScheme
    {
        public BoundingBox ExpandBounds(BoundingBox currentBounds, BoundingBox desiredBounds)
        {
            return currentBounds;
        }

        public IEnumerable<BoundingBox> Split(MeshOperator meshOperator, BoundingBox box)
        {
            List<BoundingBox> boxes = new List<BoundingBox>();
            Vector3 min = box.Min;
            Vector3 max = box.Max;
            Vector3 mid = box.Min + (box.Size() / 2);
            Vector3 extents = box.Extent();
            if (extents.X >= extents.Y && extents.X >= extents.Z)
            {
                // X is longest dimension
                boxes.Add(new BoundingBox(new Vector3(min.X, min.Y, min.Z), new Vector3(mid.X, max.Y, max.Z)));
                boxes.Add(new BoundingBox(new Vector3(mid.X, min.Y, min.Z), new Vector3(max.X, max.Y, max.Z)));
            }
            else if(extents.Y >= extents.X && extents.Y >= extents.Z)
            {
                // Y is longest dimension
                boxes.Add(new BoundingBox(new Vector3(min.X, min.Y, min.Z), new Vector3(max.X, mid.Y, max.Z)));
                boxes.Add(new BoundingBox(new Vector3(min.X, mid.Y, min.Z), new Vector3(max.X, max.Y, max.Z)));

            }
            else if(extents.Z >= extents.X && extents.Z >= extents.Y)
            {
                // Z is longest dimension
                boxes.Add(new BoundingBox(new Vector3(min.X, min.Y, min.Z), new Vector3(max.X, max.Y, mid.Z)));
                boxes.Add(new BoundingBox(new Vector3(min.X, min.Y, mid.Z), new Vector3(max.X, max.Y, max.Z)));
            }
            return boxes;
        }
    }
}
