using MathNet.Numerics.LinearAlgebra;
using OPS.MathExtensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;

namespace OPS.Geometry
{
    /// <summary>
    /// Defines uncertainty in a node's transform.
    /// </summary>
    public class NodeUncertainTransform : NodeComponent
    {
        private UncertainRigidTransform _transform;
        public UncertainRigidTransform UncertainTransform
        {
            get
            {
                return _transform;
            }
            set
            {
                _transform = value;
                Node.Transform.Matrix = value.Mean;
            }
        }

        /// <summary>
        /// Covariance matrix of transform distribution
        /// </summary>
        public Matrix<double> Covariance
        {
            get
            {
                return UncertainTransform.Distribution.Covariance;
            }
            set
            {
                // Changing covariance only doesn't effect mean values, so NodeTransform.Matrix
                // is still valid
                _transform = new UncertainRigidTransform(new GaussianND(UncertainTransform.Distribution.Mean, value));
            }
        }

        public NodeUncertainTransform()
        {
            _transform = null;
        }

        public NodeUncertainTransform(UncertainRigidTransform transform)
        {
            _transform = transform;
        }

        /// <summary>
        /// Mean value of transform (equal to NodeTransform.Matrix)
        /// </summary>
        public Matrix Mean
        {
            get
            {
                return UncertainTransform.Mean;
            }
            set
            {
                UncertainTransform = new UncertainRigidTransform(value, Covariance);
            }
        }

        public override void Initialize()
        {
            if (_transform == null)
            {
                // Initialize with perfect certainty (zero covariance matrix)
                UncertainTransform = new UncertainRigidTransform(Node.Transform.Matrix, CreateMatrix.Dense<double>(6, 6));
            }
            else
            {
                // we were constructed with a transform - overwrite what's in NodeTransform
                Node.Transform.Matrix = UncertainTransform.Mean;
            }
        }

        /// <summary>
        /// Compute the uncertain transform from this node to the root of its scenegraph.
        /// </summary>
        public UncertainRigidTransform LocalToWorld
        {
            get
            {
                // TODO: consider how best to do this. Ideally it would be lazily computed, like NodeTransform,
                // but it would need to be notified of changes to NodeTransform somehow (without substantially
                // degrading performance in the common case of no uncertainty). Also note that not all nodes
                // necessarily have a NodeUncertainTransform component - this makes a recursive implementation
                // really tricky without just force-adding one to nodes without.
                // Tracked as issue #97
                UncertainRigidTransform t = UncertainTransform;
                SceneNode current = Node;
                while (current.Parent != null)
                {
                    current = current.Parent;

                    UncertainRigidTransform next;
                    var ut = current.GetComponent<NodeUncertainTransform>();
                    if (ut != null)
                    {
                        next = ut.UncertainTransform;
                    }
                    else
                    {
                        next = new UncertainRigidTransform(current.Transform.Matrix, CreateMatrix.Dense<double>(6, 6));
                    }
                    t = t * next;
                }
                return t;
            }
        }

        /// <summary>
        /// Inverse of LocalToWorld.
        /// </summary>
        public UncertainRigidTransform WorldToLocal
        {
            get
            {
                return LocalToWorld.Inverse();
            }
        }
    }
}
