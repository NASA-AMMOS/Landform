using Microsoft.Xna.Framework;

namespace JPLOPS.MathExtensions
{
    /// <summary>
    /// Acts as a Vector3 but with integer instead of double values
    /// </summary>
    public struct Vector3Int
    {
        public int X;
        public int Y;
        public int Z;
        
        /// <summary>
        /// Constructs from a regular Vector3 by truncating decimal places
        /// </summary>
        /// <param name="vector3">Vector3 value which gets its components cast to an integer</param>
        public Vector3Int(Vector3 vector3)
        {
            X = (int)vector3.X;
            Y = (int)vector3.Y;
            Z = (int)vector3.Z;
        }
        
        /// <summary>
        /// Constructs from X, Y, and Z coordinates
        /// </summary>
        /// <param name="X">X component</param>
        /// <param name="Y">Y component</param>
        /// <param name="Z">Z component</param>
        public Vector3Int(int X, int Y, int Z)
        {
            this.X = X;
            this.Y = Y;
            this.Z = Z;
        }
        
        /// <summary>
        /// Returns a new Vector3Int of this offset by the given coordinates
        /// </summary>
        /// <param name="x">X component offset</param>
        /// <param name="y">Y component offset</param>
        /// <param name="z">Z component offset</param>
        /// <returns>New Vector3Int that is the sum of this and the given coordinates</returns>
        public Vector3Int offset(int x, int y, int z)
        {
            return new Vector3Int(X + x, Y + y, Z + z);
        }
        
        /// <summary>
        /// Generates a hash code from the coordinate components
        /// </summary>
        /// <returns>The hash code of this Vector3Int</returns>
        public override int GetHashCode()
        {
            return (((X + 17) * 10037 + Y) * 137 + Z) * 37;
        }
        
        /// <summary>
        /// Returns the equality of this Vector3Int to another
        /// </summary>
        /// <param name="obj">Object to compare this against</param>
        /// <returns>True if this has the same coordinates, false if they are different</returns>
        public override bool Equals(object obj)
        {
            Vector3Int other = (Vector3Int)obj;
            return X == other.X && Y == other.Y && Z == other.Z;
        }
    }
}

