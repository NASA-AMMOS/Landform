using MathNet.Numerics.LinearAlgebra;
using OPS.MathExtensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Geometry
{
    public class NodeTransformUncertainty : NodeComponent
    {
        // Mean is stored in NodeTransform
        public Matrix<double> Covariance;

        public NodeTransformUncertainty()
        {
            // Initialize with perfect certainty (zero covariance matrix)
            Covariance = CreateMatrix.Dense<double>(6, 6);
        }

        public UncertainRigidTransform UncertainTransform
        {
            get
            {
                return new UncertainRigidTransform(Node.Transform.Matrix, Covariance);
            }
        }

        public UncertainRigidTransform LocalToWorld
        {
            get
            {
                UncertainRigidTransform t = UncertainTransform;
                SceneNode current = Node;
                while (current.Parent != null)
                {
                    current = current.Parent;
                    var mean = current.Transform.Matrix;
                    var ut = current.GetComponent<NodeTransformUncertainty>();
                    Matrix<double> covariance;
                    if (ut != null) covariance = ut.Covariance;
                    else covariance = CreateMatrix.Dense<double>(6, 6);

                    t = t * new UncertainRigidTransform(mean, covariance);
                }
                return t;
            }
        }
        public UncertainRigidTransform WorldToLocal
        {
            get
            {
                return LocalToWorld.Inverse();
            }
        }
    }
}
