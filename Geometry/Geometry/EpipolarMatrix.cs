using Microsoft.Xna.Framework;
using System;

namespace JPLOPS.Geometry
{
    /// <summary>
    /// An epipolar transformation from a model image to a data image.
    /// </summary>
    public class EpipolarMatrix
    {
        public readonly Matrix matrix;

        /// <summary>
        /// Return the epipolar line in the data image corresponding to a
        /// point in the model image.
        /// </summary>
        /// <param name="modelPt">Model image point</param>
        /// <returns>Equation for the epipolar line in form ax + by = c</returns>
        public virtual Vector3 EpipolarLine(Vector2 modelPt)
        {
            return Vector3.Transform(new Vector3(modelPt, 1), matrix);
        }

        /// <summary>
        /// Return the inverse transform, from data to model.
        /// </summary>
        /// <returns></returns>
        public virtual EpipolarMatrix Inverse()
        {
            return new EpipolarMatrix(Matrix.Transpose(matrix));
        }

        public EpipolarMatrix(Matrix matrix)
        {
            for (int i = 0; i < 4; i++)
            {
                matrix[i, 3] = 0;
                matrix[3, i] = 0;
            }
            this.matrix = matrix;
        }

        /// <summary>
        /// Construct an epipolar transform in image coordinates from one in Moisan-Stival normalized coordinates.
        /// </summary>
        public static EpipolarMatrix Scaled(Matrix matrix, Vector2 modelSize, Vector2 dataSize)
        {
            var modelNorm = 1 / Math.Sqrt(modelSize.X * modelSize.Y);
            var dataNorm = 1 / Math.Sqrt(dataSize.X * dataSize.Y);

            var modelToNorm = new Matrix();
            modelToNorm[0, 0] = modelToNorm[1, 1] = modelNorm;
            modelToNorm[0, 2] = -modelSize.X * modelNorm / 2;
            modelToNorm[1, 2] = -modelSize.Y * modelNorm / 2;
            modelToNorm[2, 2] = 1;
            modelToNorm[3, 3] = 1;
            var dataToNorm = new Matrix();
            dataToNorm[0, 0] = dataToNorm[1, 1] = dataNorm;
            dataToNorm[0, 2] = -dataSize.X * dataNorm / 2;
            dataToNorm[1, 2] = -dataSize.Y * dataNorm / 2;
            dataToNorm[2, 2] = 1;
            dataToNorm[3, 3] = 1;
            return new EpipolarMatrix(Matrix.Transpose(dataToNorm) * matrix * modelToNorm);
        }
    }
}
