using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;

using Xna = Microsoft.Xna.Framework;

namespace MathExtensions
{
    public class Gaussian3D
    {
        public Xna.Vector3 mean;
        public Xna.Matrix covariance;

        public IEnumerable<Xna.Vector3> SigmaPoints()
        {
            if (covariance == Xna.Matrix.Identity * 0)
            {
                yield return mean;
                yield break;
            }

            Matrix nC = new DenseMatrix(3);
            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    nC[i, j] = covariance[i, j] * 3;
                }
            }
            var svd = nC.Svd();
            var U = svd.U;
            var VT = svd.VT;
            var sqrtS = svd.S.PointwiseSqrt();

            var sqrtNC = U * Matrix.Build.Diagonal(sqrtS.ToArray()) * VT;
            for (int i = 0; i < 3; i++)
            {
                var column = new Xna.Vector3(sqrtNC[0, i], sqrtNC[1, i], sqrtNC[2, i]);
                yield return mean + column;
                yield return mean - column;
            }
        }

        public Gaussian3D(IEnumerable<Xna.Vector3> points, bool population = false)
        {
            mean = Xna.Vector3.Zero;
            List<Xna.Vector3> myPts = new List<Xna.Vector3>();
            foreach (Xna.Vector3 v in points)
            {
                myPts.Add(v);
                mean += v;
            }
            mean /= myPts.Count;

            covariance = Xna.Matrix.Identity * 0;
            if (myPts.Count == 1) return;

            for (int i = 0; i < myPts.Count; i++)
            {
                var v = myPts[i];
                for (int j = 0; j < 3; j++)
                {
                    for (int k = 0; k < 3; k++)
                    {
                        covariance[j, k] += (v[j] - mean[j]) * (v[k] - mean[k]);
                    }
                }
            }
            if (population)
            {
                covariance *= 1.0 / (myPts.Count);
            }
            else
            {
                covariance *= 1.0 / (myPts.Count - 1.0);
            }
        }

        public Gaussian3D(Xna.Vector3 mean, Xna.Matrix covariance)
        {
            this.mean = mean;
            this.covariance = covariance;
        }

        public Gaussian3D Transform(Func<Xna.Vector3, Xna.Vector3> f)
        {
            return new Gaussian3D(SigmaPoints().Select(pt => f(pt)));
        }
        public Gaussian3D Transform(Func<Xna.Vector3, IEnumerable<Xna.Vector3>> f)
        {
            return new Gaussian3D(SigmaPoints().SelectMany(pt => f(pt)));
        }
    }
}
