using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Geometry.Geometry
{
    struct Vector3Int
    {
        public int X;
        public int Y;
        public int Z;

        public Vector3Int(Vector3 vector3)
        {
            X = (int)vector3.X;
            Y = (int)vector3.Y;
            Z = (int)vector3.Z;
        }

        public Vector3Int(int X, int Y, int Z)
        {
            this.X = X;
            this.Y = Y;
            this.Z = Z;
        }

        public Vector3Int offset(int x, int y, int z)
        {
            return new Vector3Int(this.X + x, this.Y + y, this.Z + z);
        }

        public override int GetHashCode()
        {
            return (((this.X + 17) * 10037 + this.Y) * 137 + this.Z) * 37;
        }

        public override bool Equals(object obj)
        {
            Vector3Int other = (Vector3Int)obj;
            return this.X == other.X && this.Y == other.Y && this.Z == other.Z;
        }
    }
}
