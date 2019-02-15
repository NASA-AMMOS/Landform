using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using log4net;
using OPS.Util;
using OPS.Imaging;
using OPS.Geometry;

namespace OPS.Alignment
{
    public class FrustumOverlapDetector
    {
        private readonly IImageLoader loader;
        private readonly ILog logger;

        public FrustumOverlapDetector(IImageLoader loader = null, ILog logger = null)
        {
            this.loader = loader;
            this.logger = logger;
        }

        public int MakeHulls(AlignmentScene scene)
        {
            int numNodes = 0, paramsHulls = 0, imageHulls = 0, unionHulls = 0, emptyHulls = 0;

            double lastSpew = UTCTime.Now();
            void spewMaybe(bool force = false)
            {
                double now = UTCTime.Now();
                if (logger != null &&  (force || now - lastSpew > 10))
                {
                    logger.InfoFormat("processed {0} nodes, " +
                                      "made {1} hulls from params, {2} from images, {3} from children, {4} empty",
                                      numNodes, paramsHulls, imageHulls, unionHulls, emptyHulls);
                    lastSpew = now;
                }
            }
                
            void make(SceneNode node)
            {
                numNodes++;

                if (node.IsLeaf && !node.HasComponent<NodeConvexHull>())
                {
                    var chc = node.AddComponent<NodeConvexHull>();
                    if (node.HasComponent<NodeImage>())
                    {
                        var img = node.GetComponent<NodeImage>();
                        if (img.CameraModel != null && img.Size.HasValue)
                        {
                            chc.Hull = ConvexHull.FromParams(img.CameraModel, img.Size.Value.X, img.Size.Value.Y);
                            paramsHulls++;
                        }
                        else if (loader != null)
                        {
                            //TODO does this really happen?
                            chc.Hull = ConvexHull.FromImage(loader.LoadImage(img.Url));
                            imageHulls++;
                        }
                        else
                        {
                            throw new Exception("could not generate camra frustum hull for node " + node.Name);
                        }
                    }
                    else
                    {
                        chc.Hull = new ConvexHull(); //empty
                        emptyHulls++;
                    }
                }

                spewMaybe();

                if (!node.IsLeaf)
                {
                    foreach (var child in node.Children)
                    {
                        make(child);
                    }
                    
                    if (!node.HasComponent<NodeConvexHull>())
                    {
                        List<ConvexHull> childHulls = new List<ConvexHull>();
                        foreach (var child in node.Children)
                        {
                            var hull = child.GetComponent<NodeConvexHull>().Hull;
                            var ut = child.GetOrAddComponent<NodeUncertainTransform>().UncertainTransform;
                            childHulls.Add(ConvexHull.Transformed(hull, ut));
                        }
                        node.AddComponent<NodeConvexHull>().Hull = ConvexHull.Union(childHulls.ToArray());
                        unionHulls++;
                    }
                }
            }

            make(scene.Root);

            spewMaybe(force: true);

            return numNodes;
        }
        
        public void Detect(AlignmentScene scene, bool allowInternalOverlaps = true)
        {
            if (logger != null)
            {
                logger.InfoFormat("detecting overlaps, {0}allowing internal overlaps",
                                 allowInternalOverlaps ? "" : "not ");
            }

            MakeHulls(scene);

            HashSet<URLPair> unique = new HashSet<URLPair>();

            double lastSpew = UTCTime.Now();
            int overlapChecks = 0, processedPairs = 0, overlappingLeaves = 0, processedNodes = 0;
            void spewMaybe(bool force = false)
            {
                double now = UTCTime.Now();
                if (logger != null && (force || (now - lastSpew > 10)))
                {
                    logger.InfoFormat("found {0} unique overlaps ({1} checks), {2} overlapping leaf pairs, " +
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

                if (nodeHull.IsEmpty) return false;
                if (otherHull.IsEmpty) return false;

                var nodeToOther = node.GetOrAddComponent<NodeUncertainTransform>().To(other);
                var thisInOther = ConvexHull.Transformed(nodeHull, nodeToOther);

                return thisInOther.Intersects(otherHull);
            }

            void processPairwise(SceneNode one, SceneNode two)
            {
                processedPairs++;

                if (one.HasComponent<NodeImage>() && two.HasComponent<NodeImage>())
                {
                    unique.Add(new URLPair(one.GetComponent<NodeImage>().Url, two.GetComponent<NodeImage>().Url));
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

            spewMaybe(force: true);

            if (logger != null)
            {
                logger.InfoFormat("done detecting overlaps, {0} unique overlaps", unique.Count);
            }

            scene.Overlaps = unique;
        }

        public void Detect(AlignmentScene scene)
        {
            Detect(scene, true);
        }
    }
}
