using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Pipeline
{
    /// <summary>
    /// Represents an affine transformation from a node to its parent frame.
    /// </summary>
    public class NodeTransform : NodeComponent
    {
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

        NodeTransform parent;
        public NodeTransform Parent
        {
            get { return parent; }
            set
            {
                if (parent != null)
                {
                    parent.children.Remove(this);
                }
                parent = value;
                if (parent != null)
                {
                    parent.children.Add(this);
                }
                localToWorldDirty = true;
            }
        }

        HashSet<NodeTransform> children;
        public IEnumerable<NodeTransform> Children
        {
            get
            {
                return children.AsEnumerable();
            }
        }

        Quaternion rotation;
        public Quaternion Rotation
        {
            get { return rotation; }
            set { rotation = value; matrixDirty = true; }
        }

        Vector3 translation;
        public Vector3 Translation
        {
            get { return translation; }
            set { translation = value; matrixDirty = true; }
        }

        Vector3 scale;
        public Vector3 Scale
        {
            get { return scale; }
            set { scale = value; matrixDirty = true; }
        }


        Matrix matrix;
        bool matrixDirty;
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
            }
        }


        Matrix localToWorld;
        bool localToWorldDirty;
        /// <summary>
        /// A matrix transforming from the node's local coordinate frame to world space.
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
        }
    }
}
