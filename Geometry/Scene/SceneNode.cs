using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Geometry
{
    public class SceneNode
    {
        public string Name;
        public readonly NodeTransform Transform;

        /// <summary>
        /// Create a new node with a given name.
        /// </summary>
        public SceneNode(string name)
        {
            components = new Dictionary<Type, object>();

            this.Name = name;
            this.Transform = AddComponent<NodeTransform>();
        }

        /// <summary>
        /// Create a new node with a given name and parent.
        /// </summary>
        public SceneNode(string name, NodeTransform parent)
                : this(name)
        {
            this.Transform.Parent = parent;
        }

        /// <summary>
        /// Create a new node with a given name, parent, and local transform.
        /// </summary>
        public SceneNode(string name, NodeTransform parent,
            Vector3 translation,
            Quaternion rotation,
            Vector3 scale)
                : this(name, parent)
        {
            this.Transform.Translation = translation;
            this.Transform.Rotation = rotation;
            this.Transform.Scale = scale;
        }


        /// <summary>
        /// Add a component of type T.
        /// </summary>
        /// <typeparam name="T">Type of component.</typeparam>
        /// <returns></returns>
        public T AddComponent<T>() where T : NodeComponent, new()
        {
            if (HasComponent<T>())
            {
                throw new InvalidOperationException("component already exists");
            }
            T res = new T();
            res.Node = this;
            components[typeof(T)] = res;
            return res;
        }

        /// <summary>
        /// Get the component of type T, adding one if not present.
        /// </summary>
        /// <typeparam name="T">Type of component.</typeparam>
        public T GetOrAddComponent<T>() where T : NodeComponent, new()
        {
            if (HasComponent<T>())
            {
                return GetComponent<T>();
            }
            return AddComponent<T>();
        }

        /// <summary>
        /// Return true if this node has a component of type T.
        /// </summary>
        /// <typeparam name="T">Type of component.</typeparam>
        public bool HasComponent<T>() where T : NodeComponent
        {
            return components.ContainsKey(typeof(T));
        }

        /// <summary>
        /// Get the component of type T attached to this node, or null if none.
        /// </summary>
        /// <typeparam name="T">Type of component to find.</typeparam>
        public T GetComponent<T>() where T : NodeComponent
        {
            if (!HasComponent<T>())
            {
                return null;
            }
            return (T)components[typeof(T)];
        }

        /// <summary>
        /// Return all instances of a given component in [this node and] its children.
        /// </summary>
        /// <typeparam name="T">Type of component to find.</typeparam>
        /// <param name="includeThis">If true, include this node in the search.</param>
        public IEnumerable<T> GetComponentsInTree<T>(bool includeThis = true) where T : NodeComponent
        {
            if (includeThis && HasComponent<T>())
            {
                yield return GetComponent<T>();
            }
            foreach (NodeTransform t in Transform.Children)
            {
                foreach (T comp in t.Node.GetComponentsInTree<T>(includeThis: true)) yield return comp;
            }
        }

        internal Dictionary<Type, object> components;

        static readonly string[] SillyNames = new string[]
        {
                "Jimmy", "Joseph", "Josef", "Harold", "Franzibald",
                "Timothy", "Li'l Greg", "Alice", "Bob", "Charlie",
                "The Big One", "Strungo", "Grumpy", "Happy", "Sleepy",
                "Dopey", "Bashful", "Sneezy", "Doc", "Doc Brown",
                "Marty McFly from Back To The Future", "Sparky", "Hugo",
                "Leonard", "Lynyrd", "Chuck", "Alyssa", "Chuckles",
                "Patricia", "Strongface", "The Danger", "George",
                "Flapjack", "Harry", "Ol' Hickory Ham Mike", "Nick",
                "Yoko", "Mortality Itself", "Penny", "Jack", "Eve"
            // TODO: expand
        };
        static Random nameRand = new Random();
        /// <summary>
        /// Create a new node with a random silly name.
        /// </summary>
        public SceneNode()
            : this(SillyNames[nameRand.Next(SillyNames.Length)])
        {
        }
    }
}
