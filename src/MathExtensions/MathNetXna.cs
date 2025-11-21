using System;
using MathNet.Numerics.LinearAlgebra;
using Microsoft.Xna.Framework;

namespace JPLOPS.MathExtensions
{
    public static class MathNetXna
    {
        public static Vector<double> ToMathNet(this Vector3 vec)
        {
            return CreateVector.Dense<double>(vec.ToDoubleArray());
        }
        public static Vector3 ToXna(this Vector<double> vec)
        {
            if (vec.Count != 3) throw new InvalidOperationException("Dimension mismatch");
            return new Vector3(vec.ToArray());
        }

        public static Vector<double> ToMathNet(this Vector2 vec)
        {
            return CreateVector.Dense(vec.ToDoubleArray());
        }

        public static Matrix ToXna(this Matrix<double> mat)
        {
            if (mat.RowCount > 4 || mat.ColumnCount != mat.RowCount)
            {
                throw new InvalidOperationException("Attempt to convert >4x4 matrix to XNA");
            }
            Matrix res = Matrix.Identity;
            for (int i = 0; i < mat.RowCount; i++)
            {
                for (int j = 0; j < mat.ColumnCount; j++)
                {
                    res[i, j] = mat[i, j];
                }
            }
            return res;
        }

        public static Matrix<double> ToMathNet(this Matrix mat, int dimension=3)
        {
            if (dimension > 4) throw new InvalidOperationException("That's more dimensions than you have");
            Matrix<double> res = CreateMatrix.Dense<double>(dimension, dimension);
            for (int i = 0; i < dimension; i++)
            {
                for (int j = 0; j < dimension; j++)
                {
                    res[i, j] = mat[i, j];
                }
            }
            return res;
        }

        public static bool IsZero(this Matrix<double> mat)
        {
            foreach (var x in mat.Enumerate(Zeros.AllowSkip))
            {
                if (x != 0) return false;
            }
            return true;
        }
    }
}
