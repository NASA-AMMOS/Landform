using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Geometry
{
    /// <summary>
    /// An epipolar transformation from a model image to a data image.
    /// </summary>
    public class EpipolarTransform
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
        public virtual EpipolarTransform Inverse()
        {
            return new EpipolarTransform(Matrix.Transpose(matrix));
        }

        public EpipolarTransform(Matrix matrix)
        {
            for (int i = 0; i < 4; i++)
            {
                matrix[i, 3] = 0;
                matrix[3, i] = 0;
            }
            this.matrix = matrix;
        }
    }

    /// <summary>
    /// An epipolar transformation with a scaling transform and offset, as used
    /// by the Moisan-Stival epipolar routine.
    /// </summary>
    public class ScaledEpipolarTransform : EpipolarTransform
    {
        public ScaledEpipolarTransform(Matrix matrix, Vector2 modelSize, Vector2 dataSize)
            : base(matrix)
        {
            this.modelSize = modelSize;
            this.dataSize = dataSize;
            this.modelNorm = 1 / Math.Sqrt(modelSize.X * modelSize.Y);
            this.dataNorm = 1 / Math.Sqrt(dataSize.X * dataSize.Y);
        }
        public readonly Vector2 modelSize, dataSize;
        public readonly double modelNorm, dataNorm;


        public override Vector3 EpipolarLine(Vector2 modelPt)
        {
            Vector3 epiline = base.EpipolarLine((modelPt - modelSize / 2) * modelNorm);
            Vector3 imageSpace = new Vector3(
                epiline.X * dataNorm,
                epiline.Y * dataNorm,
                epiline.Z - dataNorm * (epiline.X * dataSize.X + epiline.Y * dataSize.Y) / 2
                );
            return imageSpace;
        }

        public override EpipolarTransform Inverse()
        {
            return new ScaledEpipolarTransform(Matrix.Transpose(matrix), dataSize, modelSize);
        }
    }
}
