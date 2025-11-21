using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using MathNet.Numerics.LinearAlgebra;
using JPLOPS.MathExtensions;

namespace JPLOPS.Geometry
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
                _transform = new UncertainRigidTransform(new NamedGaussian(Node.Guid, UncertainTransform.Distribution.Mean, value));
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
                UncertainTransform = new UncertainRigidTransform(new NamedGaussian(Node.Guid, UncertainRigidTransform.ToVector(value), Covariance));
            }
        }

        public override void Initialize()
        {
            if (_transform == null)
            {
                // Initialize from Node.Transform with perfect certainty (zero covariance matrix)
                UncertainTransform = new UncertainRigidTransform(new NamedGaussian(Node.Guid, UncertainRigidTransform.ToVector(Node.Transform.Matrix), CreateMatrix.Dense<double>(6, 6)));
            }
            else
            {
                // we were constructed with a transform - overwrite what's in Node.Transform
                Node.Transform.Matrix = UncertainTransform.Mean;
            }
        }

        public UncertainRigidTransform To(SceneNode other)
        {
            if (other == Node) return new UncertainRigidTransform(); //identity, certain

            HashSet<SceneNode> myAncestors = new HashSet<SceneNode>();
            {
                SceneNode current = Node;
                while (current.Parent != null)
                {
                    current = current.Parent;
                    myAncestors.Add(current);
                }
            }

            ThunkContext context = new ThunkContext();

            List<Guid> toParent = new List<Guid>();
            List<Guid> toOther = new List<Guid>();

            SceneNode commonAncestor = other;
            {
                while (commonAncestor != null && !myAncestors.Contains(commonAncestor))
                {
                    toOther.Add(commonAncestor.Guid);
                    context.AddVariable(commonAncestor.Guid, commonAncestor.GetOrAddComponent<NodeUncertainTransform>().UncertainTransform.Distribution);
                    commonAncestor = commonAncestor.Parent;
                }
                if (commonAncestor == null)
                {
                    throw new InvalidOperationException("From() to disjoint node");
                }
                toOther.Reverse();

                SceneNode current = Node;
                while (current != commonAncestor)
                {
                    toParent.Add(current.Guid);
                    context.AddVariable(current.Guid, current.GetOrAddComponent<NodeUncertainTransform>().UncertainTransform.Distribution);
                    current = current.Parent;
                }
            }

            var thunk = new VectorThunk(context.RandomVariables.Keys, (ctx) =>
            {
                Matrix res = Matrix.Identity;
                foreach (var id in toParent)
                {
                    res = res * UncertainRigidTransform.ToMatrix(ctx.Get(id));
                }
                foreach (var id in toOther)
                {
                    res = res * Matrix.Invert(UncertainRigidTransform.ToMatrix(ctx.Get(id)));
                }
                return UncertainRigidTransform.ToVector(res);
            });

            return new UncertainRigidTransform(UnscentedTransform.Transform(context, thunk));
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
                        next = new UncertainRigidTransform(current.Transform.Matrix); //certain
                    }
                    t = t * next;
                }
                return t;
            }
        }

        /// <summary>
        /// Inverse of LocalToWorld.
        /// </summary>
        [System.Obsolete("Use To() instead.")]
        public UncertainRigidTransform WorldToLocal
        {
            get
            {
#pragma warning disable CS0618 // Type or member is obsolete
                return LocalToWorld.Inverse();
#pragma warning restore CS0618 // Type or member is obsolete
            }
        }
    }
}
