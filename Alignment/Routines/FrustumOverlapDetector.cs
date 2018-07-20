using OPS.Geometry;
using OPS.Imaging;
using OPS.Plumbing;
using OPS.Util;
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

        public void MakeHulls(AlignmentScene scene)
        {
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
                        
                        try
                        {
                            var imgObs = ((ObservationImageRef)imgRef).Observation;
                            chc.Hull = ConvexHull.FromParams((CameraModel)JsonHelper.FromJson(imgObs.CameraModel), imgObs.Width, imgObs.Height);
                        }
                        catch
                        {
                            var img = GetImage(imgRef);
                            chc.Hull = ConvexHull.FromImage(img);
                        }
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
                            var ut = child.GetComponent<NodeUncertainTransform>();
                            childHulls.Add(ConvexHull.Transformed(hull.Hull, ut.UncertainTransform));
                        }
                    }
                    chc.Hull = ConvexHull.Union(childHulls.ToArray());
                }
            };
            collect(scene.Root);
        }
        
        public void Detect(AlignmentScene scene)
        {
            // Initialize - make sure ImageToNode is up to date and all nodes have hulls
            MakeHulls(scene);

            // get ancestry information
            Dictionary<SceneNode, HashSet<SceneNode>> ancestors = new Dictionary<SceneNode, HashSet<SceneNode>>();
            Action<SceneNode> collect = null;
            collect = (node) =>
            {
                ancestors[node] = new HashSet<SceneNode>();
                if (node.Parent != null)
                {
                    ancestors[node].Add(node.Parent);
                    foreach (var a in ancestors[node.Parent])
                    {
                        ancestors[node].Add(a);
                    }
                }

                foreach (var child in node.Children)
                {
                    collect(child);
                }

            };
            collect(scene.Root);

            Func<SceneNode, SceneNode, bool> overlaps = (node, other) =>
            {
                var nodeHull = node.GetComponent<NodeConvexHull>().Hull;
                var otherHull = other.GetComponent<NodeConvexHull>().Hull;

                var nodeToOther = node.GetOrAddComponent<NodeUncertainTransform>().To(other);
                var thisInOther = ConvexHull.Transformed(nodeHull, nodeToOther);
                return thisInOther.Intersects(otherHull);
            };

            Dictionary<SceneNode, HashSet<SceneNode>> nodeOverlaps = new Dictionary<SceneNode, HashSet<SceneNode>>();
            Action<SceneNode, SceneNode> addOverlap = (one, two) =>
            {
                if (!nodeOverlaps.ContainsKey(one)) nodeOverlaps[one] = new HashSet<SceneNode>();
                if (!nodeOverlaps.ContainsKey(two)) nodeOverlaps[two] = new HashSet<SceneNode>();
                nodeOverlaps[one].Add(two);
                nodeOverlaps[two].Add(one);
            };

            HashSet<UnorderedImagePair> unique = new HashSet<UnorderedImagePair>();
            foreach (var node in scene.ImageToNode.Values)
            {
                var imgRef = node.GetComponent<NodeImageReference>().Reference;
                var nodeHull = node.GetComponent<NodeConvexHull>().Hull;

                Queue<SceneNode> toConsider = new Queue<SceneNode>();
                toConsider.Enqueue(scene.Root);

                while (toConsider.Count > 0)
                {
                    var other = toConsider.Dequeue();

                    if (false && other == node.Parent)
                    {
                        // HACK - don't align within SD
                        continue;
                    }

                    if (nodeOverlaps.ContainsKey(node) && nodeOverlaps[node].Contains(other))
                    {
                        // already been done
                        continue;
                    }

                    if (!ancestors[node].Contains(other) && !overlaps(node, other))
                    {
                        continue;
                    }

                    addOverlap(node, other);

                    var imgRefC = other.GetComponent<NodeImageReference>();
                    if (imgRefC != null && imgRefC.Reference != imgRef)
                    {
                        unique.Add(new UnorderedImagePair(imgRef, imgRefC.Reference));
                    }

                    foreach (var child in other.Children)
                    {
                        toConsider.Enqueue(child);
                    }                
                }                
            }
            scene.Overlaps = unique;
        }
    }
}
