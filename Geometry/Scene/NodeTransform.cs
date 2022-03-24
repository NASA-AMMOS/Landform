using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;

namespace JPLOPS.Geometry
{

    /// <summary>
    /// Represents an affine transformation from a node to its parent frame.
    /// </summary>
    public class NodeTransform : NodeComponent
    {
        private NodeTransform parent;
        private HashSet<NodeTransform> children;
        private Quaternion rotation;
        private Vector3 translation;
        private Vector3 scale;
        private Matrix matrix;
        private bool matrixDirty;
        private Matrix localToWorld;
        private bool _localToWorldDirty;

        public NodeTransform()
        {
            parent = null;
            children = new HashSet<NodeTransform>();

            translation = Vector3.Zero;
            rotation = Quaternion.Identity;
            scale = Vector3.One;

            matrix = Matrix.Identity;
            matrixDirty = false;

            localToWorld = Matrix.Identity;
            localToWorldDirty = false;
        }

        /// <summary>
        /// The transform above this node in the hierarchy. Null if this node
        /// is the root.
        /// </summary>
        public NodeTransform Parent
        {
            get { return parent; }
            set
            {
                SetParent(value);
            }
        }

        /// <summary>
        /// All immediate descendants of this node.
        /// </summary>
        public IEnumerable<NodeTransform> Children
        {
            get
            {
                return children.AsEnumerable();
            }
        }

        /// <summary>
        /// Number of immediate descendants 
        /// </summary>
        public int ChildCount
        {
            get { return this.children.Count; }
        }

        /// <summary>
        /// Returns true if this node is a leaf (has no children)
        /// </summary>
        public bool IsLeaf
        {
            get { return this.children.Count == 0; }
        }

        /// <summary>
        /// Returns true if this node is a root (has no parent)
        /// </summary>
        public bool IsRoot
        {
            get { return this.parent == null; }
        }

        public Quaternion Rotation
        {
            get { return rotation; }
            set { rotation = value; matrixDirty = true; localToWorldDirty = true; }
        }

        public Vector3 Translation
        {
            get { return translation; }
            set { translation = value; matrixDirty = true; localToWorldDirty = true; }
        }

        public Vector3 Scale
        {
            get { return scale; }
            set { scale = value; matrixDirty = true; localToWorldDirty = true; }
        }

        /// <summary>
        /// Matrix transforming this node's coordinate frame to that of its parent.
        /// 
        /// Order of operations is scale, rotate, then translate.
        /// </summary>
        public Matrix Matrix
        {
            get
            {
                if (matrixDirty)
                {
                    matrix = Matrix.CreateScale(Scale) * Matrix.CreateFromQuaternion(Rotation) * Matrix.CreateTranslation(Translation);
                    matrixDirty = false;
                }
                return matrix;
            }
            set
            {
                matrix = value;
                matrix.Decompose(out scale, out rotation, out translation);
                matrixDirty = false;
                localToWorldDirty = true;
            }
        }

        /// <summary>
        /// If true, the LocalToWorld matrix needs to be recomputed.
        /// </summary>
        bool localToWorldDirty
        {
            get { return _localToWorldDirty; }
            set
            {
                // don't recurse down the tree if there is no state change
                if (value == _localToWorldDirty) return;

                _localToWorldDirty = value;

                // only propagate filth
                if (!_localToWorldDirty) return;
                foreach (var child in children) child.localToWorldDirty = value;
            }
        }

        /// <summary>
        /// The matrix transforming from the node's local coordinate frame to world space.
        /// </summary>
        public Matrix LocalToWorld
        {
            get
            {
                if (localToWorldDirty)
                {
                    if (parent != null)
                    {
                        localToWorld = Matrix * parent.LocalToWorld;
                    }
                    else
                    {
                        localToWorld = Matrix;
                    }
                    localToWorldDirty = false;
                }
                return localToWorld;
            }
            set
            {
                Matrix = value * (parent != null ? parent.WorldToLocal : Matrix.Identity);
            }
        }

        /// <summary>
        /// The matrix transforming from world space to the coordinate frame of this node.
        /// </summary>
        public Matrix WorldToLocal
        {
            get
            {
                return Matrix.Invert(LocalToWorld);
            }
            set
            {
                LocalToWorld = Matrix.Invert(value);
            }
        }
        

        /// <summary>
        /// Set the parent of this transform.
        /// 
        /// If preserveWorldTransform is true, the world space transform of this
        /// node will be preserved. If false, the local transform will be preserved.
        /// </summary>
        /// <param name="newParent">New parent transform</param>
        /// <param name="preserveWorldTransform">Preserve world space transform</param>
        public void SetParent(NodeTransform newParent, bool preserveWorldTransform = true)
        {
            Matrix oldLocalToWorld = default(Matrix);
            if (preserveWorldTransform) oldLocalToWorld = LocalToWorld;

            if (parent != null)
            {
                parent.children.Remove(this);
            }
            parent = newParent;
            if (parent != null)
            {
                parent.children.Add(this);
            }

            if (preserveWorldTransform) LocalToWorld = oldLocalToWorld;
        }
                
        /// <summary>
        /// Traverse all leaf transforms reachable from this transform in depth first order
        /// </summary>
        /// <returns></returns>
        public IEnumerable<NodeTransform> Leaves()
        {
            Stack<NodeTransform> stack = new Stack<NodeTransform>();
            stack.Push(this);
            while (stack.Count > 0)
            {
                NodeTransform curTrans = stack.Pop();
                if (curTrans.IsLeaf)
                {
                    yield return curTrans;
                }
                else
                {
                    foreach (var child in curTrans.Children)
                    {
                        stack.Push(child);
                    }
                }
            }
        }

        /// <summary>
        /// Traverse all non-leaf transforms reachable from this transform in depth first order
        /// </summary>
        /// <returns></returns>
        public IEnumerable<NodeTransform> NonLeaves()
        {
            Stack<NodeTransform> stack = new Stack<NodeTransform>();
            if (!IsLeaf)
            {
                stack.Push(this);
            }
            while (stack.Count > 0)
            {
                NodeTransform curTrans = stack.Pop();
                yield return curTrans;
                foreach (var child in curTrans.Children)
                {
                    if (!child.IsLeaf)
                    {
                        stack.Push(child);
                    }
                }
            }
        }

        /// <summary>
        /// Perform a depth first traversal starting at this node
        /// </summary>
        /// <returns></returns>
        public IEnumerable<NodeTransform> DepthFirstTraverse()
        {
            Stack<NodeTransform> stack = new Stack<NodeTransform>();
            stack.Push(this);
            while (stack.Count > 0)
            {
                NodeTransform curTrans = stack.Pop();
                // This node is a leaf node if it has no children
                yield return curTrans;
                foreach (var child in curTrans.Children)
                {
                    stack.Push(child);
                }
            }
        }

        public int Depth()
        {
            int i = 0;
            NodeTransform parent = this.parent;
            while(parent != null)
            {
                i++;
                parent = parent.parent;
            }
            return i;
        }
    }
}
