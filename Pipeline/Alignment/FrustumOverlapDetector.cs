using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Util;
using OPS.Imaging;
using OPS.Geometry;
using OPS.Alignment;
using OPS.Pipeline.AlignmentServer;

namespace OPS.Pipeline
{
    public class FrustumOverlapDetector : PipelineRoutine, IOverlapDetector
    {
        public FrustumOverlapDetector(PipelineCore pipeline) : base(pipeline) { }

        public int MakeHulls(AlignmentScene scene)
        {
            Dictionary<SceneNode, ConvexHull> worldHull = new Dictionary<SceneNode, ConvexHull>();

            double lastSpew = UTCTime.Now();
            int numNodes = 0, existingHulls = 0, paramsHulls = 0, imageHulls = 0, unionHulls = 0;
            void make(SceneNode node)
            {
                numNodes++;

                if (node.HasComponent<NodeImageUrl>())
                {
                    if (node.ChildCount > 0)
                    {
                        throw new Exception("FrustumOverlapDetector expects image references on leaves only");
                    }

                    var imgUrl = node.GetComponent<NodeImageUrl>().Url;

                    if (!scene.ImageToNode.ContainsKey(imgUrl))
                    {
                        scene.ImageToNode[imgUrl] = node;
                    }

                    if (!node.HasComponent<NodeConvexHull>())
                    {
                        var chc = node.AddComponent<NodeConvexHull>();
                        bool added = false;
                        try
                        {
                            if (node.HasComponent<NodeObservation>())
                            {
                                var obs = node.GetComponent<NodeObservation>().observation;
                                chc.Hull = ConvexHull.FromParams((CameraModel)JsonHelper.FromJson(obs.CameraModel),
                                                                 obs.Width, obs.Height);
                                added = true;
                                paramsHulls++;
                            }
                        }
                        catch { }
                        if (!added)
                        {
                            chc.Hull = ConvexHull.FromImage(pipeline.LoadImage(imgUrl));
                            imageHulls++;
                        }
                    }
                    else
                    {
                        existingHulls++;
                    }
                }

                double now = UTCTime.Now();
                if (now - lastSpew > 10)
                {
                    pipeline.LogInfo("processed {0} nodes, {1} had hulls, made {2} hulls from params, {3} from images, "
                                     + " {4} from union of child hulls",
                                     numNodes, existingHulls, paramsHulls, imageHulls, unionHulls);
                    lastSpew = now;
                }

                if (node.IsLeaf)
                {
                    return;
                }

                foreach (var child in node.Children)
                {
                    make(child);
                }

                if (!node.HasComponent<NodeConvexHull>())
                {
                    List<ConvexHull> childHulls = new List<ConvexHull>();
                    foreach (var child in node.Children)
                    {
                        var hull = child.GetComponent<NodeConvexHull>();
                        Debug.Assert(hull != null);
                        var ut = child.GetOrAddComponent<NodeUncertainTransform>();
                        childHulls.Add(ConvexHull.Transformed(hull.Hull, ut.UncertainTransform));
                    }
                    if (childHulls.Count > 0)
                    {
                        var chc = node.AddComponent<NodeConvexHull>();
                        chc.Hull = ConvexHull.Union(childHulls.ToArray());
                        unionHulls++;
                    }
                }
            }

            make(scene.Root);

            pipeline.LogInfo("{0} total nodes, {1} had hulls, made {2} hulls from params, {3} from images, "
                             + " {4} from union of child hulls",
                             numNodes, existingHulls, paramsHulls, imageHulls, unionHulls);

            return numNodes;
        }
        
        public void Detect(AlignmentScene scene, bool allowInternalOverlaps = true)
        {
            pipeline.LogInfo("detecting overlaps, {0}allowing internal overlaps", allowInternalOverlaps ? "" : "not ");

            // make sure ImageToNode is up to date and all nodes have hulls
            MakeHulls(scene);

            double lastSpew = UTCTime.Now();
            int overlapChecks = 0, processedPairs = 0, overlappingLeaves = 0, processedNodes = 0;
            HashSet<URLPair> unique = new HashSet<URLPair>();

            void spewMaybe(bool force = false)
            {
                double now = UTCTime.Now();
                if (force || (now - lastSpew > 10))
                {
                    pipeline.LogInfo("found {0} unique overlaps ({1} checks), {2} overlapping leaf pairs, " +
                                     "processed {3} pairs, {4} nodes",
                                     unique.Count, overlapChecks, overlappingLeaves, processedPairs, processedNodes);
                    lastSpew = now;
                }
            }

            bool doesOverlap(SceneNode node, SceneNode other)
            {
                overlapChecks++;

                if (!node.HasComponent<NodeConvexHull>()) return false;
                if (!other.HasComponent<NodeConvexHull>()) return false;

                var nodeHull = node.GetComponent<NodeConvexHull>().Hull;
                var otherHull = other.GetComponent<NodeConvexHull>().Hull;

                var nodeToOther = node.GetOrAddComponent<NodeUncertainTransform>().To(other);
                var thisInOther = ConvexHull.Transformed(nodeHull, nodeToOther);

                return thisInOther.Intersects(otherHull);
            }

            void processPairwise(SceneNode one, SceneNode two)
            {
                processedPairs++;

                // Debug.Assert(doesOverlap(ci, cj));

                if (one.HasComponent<NodeImageUrl>() && two.HasComponent<NodeImageUrl>())
                {
                    unique.Add(new URLPair(one.GetComponent<NodeImageUrl>().Url, two.GetComponent<NodeImageUrl>().Url));
                    overlappingLeaves++;
                }

                spewMaybe();

                if (!one.IsLeaf)
                {
                    SceneNode[] children = one.Children.ToArray();
                    for (int i = 0; i < children.Length; i++)
                    {
                        if (doesOverlap(children[i], two))
                        {
                            processPairwise(children[i], two);
                        }
                    }
                }
                else if (!two.IsLeaf)
                {
                    // this could be processPairwise(two, one) but could double
                    // stack size in the worst case
                    SceneNode[] children = two.Children.ToArray();
                    for (int i = 0; i < children.Length; i++)
                    {
                        if (doesOverlap(children[i], one))
                        {
                            processPairwise(children[i], one);
                        }
                    }
                }
            }

            void processNode(SceneNode node)
            {
                processedNodes++;
                spewMaybe();
                SceneNode[] children = node.Children.ToArray();
                for (int i = 0; i < children.Length; i++)
                {
                    var child = children[i];
                    for (int j = i + 1; j < children.Length; j++)
                    {
                        if (doesOverlap(child, children[j]))
                        {
                            processPairwise(child, children[j]);
                        }
                    }

                    if (allowInternalOverlaps)
                    {
                        processNode(child);
                    }
                }
            }
            processNode(scene.Root);

            spewMaybe(true);

            pipeline.LogInfo("done detecting overlaps, {0} unique overlaps", unique.Count);

            scene.Overlaps = unique;
        }

        public void Detect(AlignmentScene scene)
        {
            Detect(scene, true);
        }
    }
}
