using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;

namespace OPS.MathExtensions
{
    public static class XNAExtensions
    {
        /// <summary>
        /// convert XNA Matrix to row major 16 element array
        /// </summary>
        public static double[] ToDoubleArray(this Matrix mat)
        {
            return Matrix.TodoubleArray(mat); //sic
        }

        /// <summary>
        /// convert row major 16 element array to XNA Matrix  
        /// </summary>
        public static Matrix MatrixFromArray(double[] mat)
        {
            return new Matrix(mat[0], mat[1], mat[2], mat[3],
                              mat[4], mat[5], mat[6], mat[7],
                              mat[8], mat[9], mat[10], mat[11],
                              mat[12], mat[13], mat[14], mat[15]);
        }
    }
}
