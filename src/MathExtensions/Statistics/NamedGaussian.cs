using System;
using MathNet.Numerics.LinearAlgebra;

namespace JPLOPS.MathExtensions
{
    public class NamedGaussian : GaussianND
    {
        public readonly Guid Guid;

        public NamedGaussian(Vector<double> mean, Matrix<double> covariance) : base(mean, covariance)
        {
            Guid = Guid.NewGuid();
        }

        public NamedGaussian(Guid guid, Vector<double> mean, Matrix<double> covariance) : base(mean, covariance)
        {
            Guid = guid;
        }
    }
}
