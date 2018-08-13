using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using OPS.Geometry;

namespace OPS.Pipeline
{
    public class QuadTreeTilingScheme : ITilingScheme
    {

        public SkirtMode Direction { get; private set;  }

        /// <summary>
        /// Split direction defines the axis that will not be subdivided
        /// </summary>
        /// <param name="direction"></param>
        public QuadTreeTilingScheme(SkirtMode direction)
        {
            this.Direction = direction;
        }

        /// <summary>
        /// Split a parent bounding box into a list of children
        /// </summary>
        /// <param name="meshOperator"></param>
        /// <param name="box"></param>
        /// <returns></returns>
        public IEnumerable<BoundingBox> Split(MeshOperator meshOperator, BoundingBox box)
        {
            List<BoundingBox> boxes = new List<BoundingBox>();
            if (Direction == SkirtMode.Z)
            {

                // Split in the XY dimension to produce 4 boxes
                // C D
                // A B
                Vector3 mid = box.Min + (box.Size() / 2);                
                boxes.Add(new BoundingBox(new Vector3(box.Min.X, box.Min.Y, box.Min.Z), new Vector3(mid.X, mid.Y, box.Max.Z)));
                boxes.Add(new BoundingBox(new Vector3(mid.X, box.Min.Y, box.Min.Z), new Vector3(box.Max.X, mid.Y, box.Max.Z)));
                boxes.Add(new BoundingBox(new Vector3(box.Min.X, mid.Y, box.Min.Z), new Vector3(mid.X, box.Max.Y, box.Max.Z)));
                boxes.Add(new BoundingBox(new Vector3(mid.X, mid.Y, box.Min.Z), new Vector3(box.Max.X, box.Max.Y, box.Max.Z)));               
            }
            else if(Direction == SkirtMode.Y)
            {
                // Split in the XZ dimension to produce 4 boxes
                // C D
                // A B
                Vector3 mid = box.Min + (box.Size() / 2);
                boxes.Add(new BoundingBox(new Vector3(box.Min.X, box.Min.Y, box.Min.Z), new Vector3(mid.X, box.Max.Y, mid.Z)));
                boxes.Add(new BoundingBox(new Vector3(mid.X, box.Min.Y, box.Min.Z), new Vector3(box.Max.X, box.Max.Y, mid.Z)));
                boxes.Add(new BoundingBox(new Vector3(box.Min.X, box.Min.Y, mid.Z), new Vector3(mid.X, box.Max.Y, box.Max.Z)));
                boxes.Add(new BoundingBox(new Vector3(mid.X, box.Min.Y, mid.Z), new Vector3(box.Max.X, box.Max.Y, box.Max.Z)));
            }
            else
            {
                throw new NotImplementedException();
            }
            if(meshOperator == null)
            {
                return boxes;
            }
            // Filter empty quadrants. 
            return boxes.Where(b => !meshOperator.Empty(b)).ToList();
        }

        /// <summary>
        /// Expand current bound to match desired bounds as closley as possible, but only allow
        /// expansion along the split direction as other axis are fixed along tile boundaries
        /// This is important so we don't artificially clip data off the top and bottom of the tile
        /// when decimation causes the mesh to expand slightly in the vertical dimension.
        /// </summary>
        /// <param name="currentBounds"></param>
        /// <param name="desiredBounds"></param>
        /// <returns></returns>
        public BoundingBox ExpandBounds(BoundingBox currentBounds, BoundingBox desiredBounds)
        {
            // Allow expansion in the z direction
            if (Direction == SkirtMode.Z)
            {
                currentBounds.Min.Z = Math.Min(currentBounds.Min.Z, desiredBounds.Min.Z);
                currentBounds.Max.Z = Math.Max(currentBounds.Max.Z, desiredBounds.Max.Z);
                return currentBounds;
            }
            else if (Direction == SkirtMode.Y)
            {
                currentBounds.Min.Y = Math.Min(currentBounds.Min.Y, desiredBounds.Min.Y);
                currentBounds.Max.Y = Math.Max(currentBounds.Max.Y, desiredBounds.Max.Y);
                return currentBounds;
            }
            else
            {
                throw new NotImplementedException();
            }
        }
        
    }
}
