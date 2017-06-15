using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;

namespace OPS.Geometry
{
    // TODO: replace with upcoming Pipeline/Scene/SceneNode
    public class SceneNode
    {
        public BoundingBox Bounds;
        public Matrix Transform = Matrix.Identity;
        public SceneNode Parent;
        public List<SceneNode> Children = new List<SceneNode>();
        public List<SceneContent> Content = new List<SceneContent>();

        public void AddChild(SceneNode child)
        {
            child.Parent = this;
            Children.Add(child);
        }

        public IEnumerable<SceneNode> LeafNodes()
        {
            Stack<SceneNode> stack = new Stack<SceneNode>();
            stack.Push(this);
            while(stack.Count > 0)
            {
                SceneNode curNode = stack.Pop();
                // This node is a leaf node if it has no children
                if(curNode.IsLeaf)
                {
                    yield return curNode;
                }
                else
                {
                    foreach(var child in curNode.Children)
                    {
                        stack.Push(child);
                    }
                }
            }
        }

        public T GetContent<T>() where T : SceneContent
        {
            var result = this.Content.Find(c => c.GetType() == typeof(T));
            if(result != null)
            {
                return (T)result;
            }
            return null;
        }

        public List<T> GetContents<T>() where T: SceneContent
        {
            return this.Content.Where(c => c.GetType() == typeof(T)).Select(c => (T)c).ToList();
        }

        public IEnumerable<SceneNode> DepthFirstTraverse()
        {
            Stack<SceneNode> stack = new Stack<SceneNode>();
            stack.Push(this);
            while (stack.Count > 0)
            {
                SceneNode curNode = stack.Pop();
                // This node is a leaf node if it has no children
                yield return curNode;
                foreach (var child in curNode.Children)
                {
                    stack.Push(child);
                }                
            }
        }
        
        public bool IsLeaf
        {
            get { return this.Children.Count == 0; }
        }
    }
}
