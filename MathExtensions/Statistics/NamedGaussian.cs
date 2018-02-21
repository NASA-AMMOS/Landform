using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MathNet.Numerics.LinearAlgebra;

namespace OPS.MathExtensions
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
