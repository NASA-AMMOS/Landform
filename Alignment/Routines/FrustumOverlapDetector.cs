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
                            var img = Pipeline.Load(imgRef);
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
                    List<ConvexHull> childHulls = new List<ConvexHull>();
                    foreach (var child in node.Children)
                    {
                        var hull = child.GetComponent<NodeConvexHull>();
                        if (hull != null)
                        {
                            var ut = child.GetOrAddComponent<NodeUncertainTransform>();
                            childHulls.Add(ConvexHull.Transformed(hull.Hull, ut.UncertainTransform));
                        }
                        else
                        {
                            Debug.Write("no hulls");
                        }
                    }
                    if (childHulls.Count > 0)
                    {
                        var chc = node.AddComponent<NodeConvexHull>();
                        chc.Hull = ConvexHull.Union(childHulls.ToArray());
                    }
                }
            };
            collect(scene.Root);
        }
        
        public void Detect(AlignmentScene scene, bool allowInternalOverlaps=true)
        {
            // Initialize - make sure ImageToNode is up to date and all nodes have hulls
            MakeHulls(scene);
           
            Func<SceneNode, SceneNode, bool> overlaps = (node, other) =>
            {
                if (!node.HasComponent<NodeConvexHull>()) return false;
                if (!other.HasComponent<NodeConvexHull>()) return false;
                var nodeHull = node.GetComponent<NodeConvexHull>().Hull;
                var otherHull = other.GetComponent<NodeConvexHull>().Hull;

                var nodeToOther = node.GetOrAddComponent<NodeUncertainTransform>().To(other);
                var thisInOther = ConvexHull.Transformed(nodeHull, nodeToOther);
                return thisInOther.Intersects(otherHull);
            };

            HashSet<UnorderedImagePair> unique = new HashSet<UnorderedImagePair>();
            void processPairwise(SceneNode one, SceneNode two)
            {
                // Debug.Assert(overlaps(ci, cj));

                if (one.HasComponent<NodeImageReference>() && two.HasComponent<NodeImageReference>())
                {
                    unique.Add(new UnorderedImagePair(one.GetComponent<NodeImageReference>().Reference, two.GetComponent<NodeImageReference>().Reference));
                }

                if (!one.IsLeaf)
                {
                    SceneNode[] children = one.Children.ToArray();
                    for (int i = 0; i < children.Length; i++)
                    {
                        if (!overlaps(children[i], two)) continue;
                        processPairwise(children[i], two);
                    }
                }
                else if (!two.IsLeaf)
                {
                    // this could be processPairwise(two, one) but could double
                    // stack size in the worst case
                    SceneNode[] children = two.Children.ToArray();
                    for (int i = 0; i < children.Length; i++)
                    {
                        if (!overlaps(children[i], one)) continue;
                        processPairwise(children[i], one);
                    }
                }
                // else if (one.IsLeaf && two.IsLeaf) {}
            }

            void processNode(SceneNode node)
            {
                SceneNode[] children = node.Children.ToArray();
                for (int i = 0; i < children.Length; i++)
                {
                    var ci = children[i];
                    for (int j = i + 1; j < children.Length; j++)
                    {
                        var cj = children[j];
                        if (!overlaps(ci, cj)) continue;
                        processPairwise(ci, cj);
                    }

                    if (allowInternalOverlaps)
                    {
                        processNode(ci);
                    }
                }
            }
            processNode(scene.Root);
            scene.Overlaps = unique;
        }

        public void Detect(AlignmentScene scene)
        {
            Detect(scene, true);
        }
    }
}
