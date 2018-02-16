using OPS.Geometry;
using OPS.Imaging;
using OPS.Plumbing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Alignment
{
    public class FrustumOverlapDetector : PipelineRoutine, IOverlapDetector
    {
        public FrustumOverlapDetector(PipelineCore pipeline)
            : base(pipeline)
        {
        }
        
        public void Detect(AlignmentScene scene)
        {
            // Initialize - make sure ImageToNode is up to date and all nodes have hulls
            Dictionary<SceneNode, ConvexHull> worldHull = new Dictionary<SceneNode, ConvexHull>();
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
                        chc.Hull = ConvexHull.FromImage(GetImage(imgRef));
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
                        var hull = child.GetComponent<NodeConvexHull>();
                        if (hull != null)
                        {
                            var ut = child.GetOrAddComponent<NodeUncertainTransform>();
                            childHulls.Add(ConvexHull.Transformed(hull.Hull, ut.UncertainTransform));
                        }
                    }
                    chc.Hull = ConvexHull.Union(childHulls.ToArray());
                }
            };
            collect(scene.Root);

            HashSet<UnorderedImagePair> unique = new HashSet<UnorderedImagePair>();
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
                    
                    var nodeToWorld = node.GetOrAddComponent<NodeUncertainTransform>().LocalToWorld;
                    var otherToWorld = other.GetOrAddComponent<NodeUncertainTransform>().LocalToWorld;
                    var nodeToOther = nodeToWorld.TimesInverse(otherToWorld);
                    var thisInOther = ConvexHull.Transformed(nodeHull, nodeToOther);

                    if (!thisInOther.Intersects(otherHull)) continue;
                    var otherRef = other.GetComponent<NodeImageReference>();
                    if (otherRef != null && otherRef.Reference != imgRef)
                    {
                        unique.Add(new UnorderedImagePair(imgRef, otherRef.Reference));
                    }

                    foreach (var child in other.Children)
                    {
                        toConsider.Enqueue(child);
                    }
                }
            }
            scene.Context.Overlaps = unique;
        }
    }
}
