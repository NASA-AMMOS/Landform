using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using OPS.Geometry;

namespace OPS.Pipeline
{
    class OctreeTilingScheme : ITilingScheme
    {
        public BoundingBox ExpandBounds(BoundingBox currentBounds, BoundingBox? desiredBounds)
        {
            return currentBounds;
        }

        public IEnumerable<BoundingBox> Split(MeshOperator meshOperator, BoundingBox box)
        {   
            //     / G H
            //    /  E F
            //   /     /      Y
            //   C D  /
            //   A B /
            //
            //    X
            List<BoundingBox> boxes = new List<BoundingBox>();
            Vector3 min = box.Min;
            Vector3 max = box.Max;
            Vector3 mid = box.Min + (box.Size() / 2);
            // A
            boxes.Add(new BoundingBox(new Vector3(min.X, min.Y, min.Z), new Vector3(mid.X, mid.Y, mid.Z)));
            // B
            boxes.Add(new BoundingBox(new Vector3(mid.X, min.Y, min.Z), new Vector3(max.X, mid.Y, mid.Z)));
            // C
            boxes.Add(new BoundingBox(new Vector3(min.X, mid.Y, min.Z), new Vector3(mid.X, max.Y, mid.Z)));
            // D
            boxes.Add(new BoundingBox(new Vector3(mid.X, mid.Y, min.Z), new Vector3(max.X, max.Y, mid.Z)));
            // E
            boxes.Add(new BoundingBox(new Vector3(min.X, min.Y, mid.Z), new Vector3(mid.X, mid.Y, max.Z)));
            // F
            boxes.Add(new BoundingBox(new Vector3(mid.X, min.Y, mid.Z), new Vector3(max.X, mid.Y, max.Z)));
            // G
            boxes.Add(new BoundingBox(new Vector3(min.X, mid.Y, mid.Z), new Vector3(mid.X, max.Y, max.Z)));
            // H
            boxes.Add(new BoundingBox(new Vector3(mid.X, mid.Y, mid.Z), new Vector3(max.X, max.Y, max.Z)));
            return boxes;
        }
    }
}
