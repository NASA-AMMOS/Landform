using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using JPLOPS.Util;

namespace JPLOPS.Geometry
{
    public class SceneNode
    {
        public string Name;
        public readonly NodeTransform Transform;
        public readonly Guid Guid;

        /// <summary>
        /// Create a new node with a given name.
        /// </summary>
        public SceneNode(string name)
        {
            components = new Dictionary<Type, NodeComponent>();

            this.Name = name;
            this.Transform = AddComponent<NodeTransform>();
            this.Guid = Guid.NewGuid();
        }

        /// <summary>
        /// Create a new node with a given name and parent.
        /// </summary>
        public SceneNode(string name, NodeTransform parent) : this(name)
        {
            this.Transform.Parent = parent;
        }

        /// <summary>
        /// Create a new node with a given name, parent, and local transform.
        /// </summary>
        public SceneNode(string name, NodeTransform parent, Vector3 translation, Quaternion rotation, Vector3 scale)
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
            return (T)AddComponent(new T());
        }

        public NodeComponent AddComponent(NodeComponent component)
        {
            if (component.Node != null)
            {
                throw new InvalidOperationException("component already attached to a node");
            }

            Type t = component.GetType();
            if (components.ContainsKey(t))
            {
                throw new InvalidOperationException("component already exists");
            }

            components[t] = component;
            components[t].Node = this;
            components[t].Initialize();
            return components[t];
        }
        
        /// <summary>
        /// Add a component of type T.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="res">A component object to add</param>
        /// <returns></returns>
        public T AddComponent<T>(T res) where T : NodeComponent, new()
        {
            if (HasComponent<T>())
            {
                throw new InvalidOperationException("component already exists");
            }
            res.Node = this;
            components[typeof(T)] = res;
            res.Initialize();
            return res;
        }

        /// <summary>
        /// Removes a compontnet of type T
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns>Returns the removed component if this node has one, null otherwise</returns>
        public T RemoveComponent<T>() where T : NodeComponent
        { 
            if (HasComponent<T>())
            {
                T res = (T)components[typeof(T)];
                components.Remove(typeof(T));
                return res;
            }
            return null;
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

        internal Dictionary<Type, NodeComponent> components;

        static readonly string[] SillyNames = new string[]
        {
                "Jimmy", "Joseph", "Josef", "Harold", "Franzibald",
                "Timothy", "Li'l Greg", "Alice", "Bob", "Charlie",
                "The Big One", "Strungo", "Grumpy", "Happy", "Sleepy",
                "Dopey", "Bashful", "Sneezy", "Doc", "Doc Brown", "Kevin Bacon",
                "Marty McFly from Back To The Future", "Sparky", "Hugo",
                "Leonard", "Lynyrd", "Chuck", "Alyssa", "Chuckles", "Mick",
                "Patricia", "Strongface", "The Danger", "George", "Donald",
                "Flapjack", "Harry", "Ol' Hickory Ham Mike", "Nick", "Thom", "Watman",
                "Yoko", "Mortality Itself", "Penny", "Jack", "Rocky", "Bill", "Ted", "Eve"
        };

        static readonly string[] SillyDescriptors = new string[]
        {
            "Desolate", "Yellow", "High", "Gigantic", "Icy", "Dusty", "Dry", "Humid", "Nice", "Hilly", "Rocky", "Orange", "Sunny", "Cloudy", "Dull", "Smoky", "Foggy", "Ancient", "Granite", "Boring", "Exciting", "Huge", "Excellent", "Dramatic"
        };

        static readonly string[] LessSillyPlaces = new string[]
        {
            "Flats", "Plains", "Butte", "Highlands", "Desert", "Mountain", "Savannah", "Ocean", "Taiga", "Valley", "Tundra", "Mesa", "Canyon", "Ridge", "Cliff Face", "Riverbed", "Gorge", "Rock", "Swamp", "Adventure", "Slope", "Descent", "Erg",
            "Barchan", "Dunes", "Dreikanter", "Ventifact", "Yardang", "Palsa", "Fjord", "Marsh", "Inselberg", "Gully", "Gulch", "Cuesta", "Hogback", "Hoodoos", "Arroyo", "Yazoo Stream", "Terrace", "Ravine", "Dome", "Crater", "Cryovolcano",
            "Bornhardt", "Karst Field"
        };

        static Random nameRand = NumberHelper.MakeRandomGenerator();
        /// <summary>
        /// Create a new node with a random silly name.
        /// </summary>
        public SceneNode()
            : this(SillyNames[nameRand.Next(SillyNames.Length)] + " and " + SillyNames[nameRand.Next(SillyNames.Length)] + "'s " + SillyDescriptors[nameRand.Next(SillyDescriptors.Length)] + " " + LessSillyPlaces[nameRand.Next(LessSillyPlaces.Length)])
        {
        }

        /// <summary>
        /// All immediate descendants of this node.
        /// </summary>
        public IEnumerable<SceneNode> Children
        {
            get
            {
                return Transform.Children.Select(t => t.Node);
            }
        }

        /// <summary>
        /// Number of immediate descendants 
        /// </summary>
        public int ChildCount
        {
            get { return Transform.ChildCount; }
        }

        /// <summary>
        /// Get or set this node's parent.
        /// When setting the parent, this node's world transform is not preserved, but its local transform is.
        /// If you want the opposite, use Transform.SetParent().
        /// </summary>
        public SceneNode Parent
        {
            get
            {
                if (Transform.Parent == null)
                {
                    return null;
                }
                return Transform.Parent.Node;
            }
            set
            {
                if (value != null)
                {
                    Transform.SetParent(value.Transform, preserveWorldTransform: false);
                }
                else
                {
                    Transform.SetParent(null, preserveWorldTransform: false);
                }
            }
        }

        /// <summary>
        /// Returns true if this node is a leaf (has no children)
        /// </summary>
        public bool IsLeaf
        {
            get { return Transform.IsLeaf;  }
        }

        /// <summary>
        /// Returns true if this node is a root (has no parent)
        /// </summary>
        public bool IsRoot
        {
            get { return Transform.IsRoot;  }
        }

        /// <summary>
        /// Traverse all leaf nodes reachable from this node in depth first order
        /// </summary>
        /// <returns></returns>
        public IEnumerable<SceneNode> Leaves()
        {
            foreach(NodeTransform t in Transform.Leaves())
            {
                yield return t.Node; 
            }
        }

        /// <summary>
        /// Traverse all non-leaf nodes reachable from this node in depth first order
        /// </summary>
        /// <returns></returns>
        public IEnumerable<SceneNode> NonLeaves()
        {
            foreach(NodeTransform t in Transform.NonLeaves())
            {
                yield return t.Node; 
            }
        }

        /// <summary>
        /// Perform a breadth first traversal starting at this node
        /// </summary>
        /// <returns></returns>
        public IEnumerable<SceneNode> DepthFirstTraverse()
        {
            foreach(NodeTransform t in Transform.DepthFirstTraverse())
            {
                yield return t.Node;
            }
        }
    }
}
