using OPS.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Alignment
{
    public class FrustumOverlapDetector : IOverlapDetector
    {
        public void Detect(AlignmentScene scene)
        {
            // Initialize - make sure ImageToNode is up to date and all nodes have hulls
            Action<SceneNode> collect = null;
            collect = (node) =>
            {
                // Node has an image
                if (node.HasComponent<NodeImageReference>())
                {
                    if (node.ChildCount > 0)
                    {
                        throw new Exception("FrustumOverlapDetector expects image references on leaves only");
                    }

                    var imgRef = node.GetComponent<NodeImageReference>().Reference;

                    if (!scene.ImageToNode.ContainsKey(imgRef))
                    {
                        scene.ImageToNode[imgRef] = node;
                    }

                    if (!node.HasComponent<NodeConvexHull>())
                    {
                        var chc = node.AddComponent<NodeConvexHull>();
                        chc.Hull = ConvexHull.FromImage(imgRef.Image);
                    }
                    return;
                }

                foreach (var child in node.Children)
                {
                    collect(child);
                }

                if (!node.HasComponent<NodeConvexHull>())
                {
                    var chc = node.AddComponent<NodeConvexHull>();
                    List<ConvexHull> childHulls = new List<ConvexHull>();
                    foreach (var child in node.Children)
                    {
                        if (child.HasComponent<NodeConvexHull>())
                        {
                            childHulls.Add(child.GetComponent<NodeConvexHull>().Hull);
                        }
                    }
                    chc.Hull = ConvexHull.Union(childHulls.ToArray());
                }
            };
            collect(scene.Root);

            foreach (var imgRef in scene.ImageToNode.Keys)
            {
                var node = scene.ImageToNode[imgRef];
                var nodeHull = node.GetComponent<NodeConvexHull>().Hull;

                Queue<SceneNode> toConsider = new Queue<SceneNode>();
                toConsider.Enqueue(scene.Root);
                while (toConsider.Count > 0)
                {
                    var other = toConsider.Dequeue();
                    var otherHull = other.GetComponent<NodeConvexHull>().Hull;

                    if (!nodeHull.Intersects(otherHull)) continue;
                    var otherRef = other.GetComponent<NodeImageReference>();
                    if (otherRef != null)
                    {
                        scene.Overlaps.Add(new KeyValuePair<Imaging.ImageRef, Imaging.ImageRef>(imgRef, otherRef.Reference));
                    }

                    foreach (var child in other.Children)
                    {
                        toConsider.Enqueue(child);
                    }
                }
            }
        }
    }
}
