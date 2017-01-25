using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;

namespace OPS.Geometry
{
    public class Vertex
    {
        /// <summary>
        /// Required property of all Vetex objects
        /// </summary>
        public Vector3 Position;
        /// <summary>
        /// Optional property
        /// </summary>
        public Vector3 Normal;
        /// <summary>
        /// Optional property
        /// </summary>
        public Vector3 Color;
        /// <summary>
        /// Optional property
        /// </summary>
        public Vector2 UV;

        public Vertex()
        {

        }

        public Vertex(Vertex other)
        {
            this.Position = other.Position;
            this.Normal = other.Normal;
            this.Color = other.Color;
            this.UV = other.UV;
        }

        public override bool Equals(System.Object obj)
        {
            return Equals(obj as Vertex);
        }

        public bool Equals(Vertex v)
        {
            // For Equals implementation see https://msdn.microsoft.com/en-us/library/dd183755.aspx 
            // If parameter is null, return false.
            if (Object.ReferenceEquals(v, null))
            {
                return false;
            }

            // Optimization for a common success case.
            if (Object.ReferenceEquals(this, v))
            {
                return true;
            }

            // If run-time types are not exactly the same, return false.
            if (this.GetType() != v.GetType())
            {
                return false;
            }

            // Return true if the fields match.
            // Note that the base class is not invoked because it is
            // System.Object, which defines Equals as reference equality.
            return (Position == v.Position) && (Normal == v.Normal) && (Color == v.Color) && (UV == v.UV);
        }


        public override int GetHashCode()
        {
            int uvValue = uvValue = ((int)UV.X * 100) ^ ((int)UV.Y * 100);
            return  ((int)Position.X) ^ ((int)Position.Y*10) ^ ((int)Position.Z*100) ^ uvValue;
        }
    }
}
