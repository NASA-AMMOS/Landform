using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using log4net;
using OPS.Util;
using OPS.Cloud;
using OPS.Imaging;
using OPS.Geometry;
using OPS.Alignment;
using OPS.Pipeline;
using OPS.Pipeline.AlignmentServer;

namespace OPS.Pipeline
{
    public class BundleAdjusting
    {
        public static AlignmentScene BundleAdjust(PipelineCore pipeline, string projectName,
                                                  bool adjustWithinSiteDrives = false,
                                                  bool adjustAcrossSiteDrives = true,
                                                  Func<Observation, bool> observationFilter = null,
                                                  int rounds = 2,
                                                  string debugOutputFolder = null)
        {
            var project = Project.Find(pipeline, projectName);

            pipeline.LogInfo("building scene graph for bundle adjustment, project {0}", projectName);
            var bsg = new BuildSceneGraph(pipeline, project.Name, new BuildSceneGraph.Options {
                    UseTransformPriors = true,
                    LoadFeatures = true,
                    LoadCorrespondences = true,
                    OnlyKeepImagesWithFeatures = true,
                    OnlyKeepBestImages = true,
                    OnlyCrossSiteDriveOverlaps = !adjustWithinSiteDrives,
                    IncludeObservation = obs => observationFilter == null || observationFilter(obs)
                });
            AlignmentScene scene = bsg.BuildTopDown(project.RootFrame);

            int numAdjustedNodes = 0, numImageNodes = 0, nsd = 0, nobs = 0;
            foreach (var siteDriveNode in scene.Root.Children)
            {
                Debug.Assert(!siteDriveNode.IsLeaf);
                Debug.Assert(!siteDriveNode.HasComponent<NodeImage>());
                if (adjustAcrossSiteDrives)
                {
                    siteDriveNode.AddComponent<AdjustedNode>();
                    numAdjustedNodes++;
                    nsd++;
                }
                foreach (var observationNode in siteDriveNode.Children)
                {
                    Debug.Assert(observationNode.IsLeaf);
                    if (observationNode.HasComponent<NodeImage>())
                    {
                        numImageNodes++;
                        if (adjustWithinSiteDrives)
                        {
                            observationNode.AddComponent<AdjustedNode>();
                            numAdjustedNodes++;
                            nobs++;
                        }
                    }
                }
            }

            pipeline.LogInfo("adjusting across site drives: {0}", adjustAcrossSiteDrives);
            pipeline.LogInfo("adjusting within site drives: {0}", adjustWithinSiteDrives);
            pipeline.LogInfo("adjusting {0} nodes, {1} site drive frames, {2} observation frames", 
                             numAdjustedNodes, nsd, nobs);

            if (numAdjustedNodes >= 2)
            {
                pipeline.LogInfo("running bundle adjuster, {0} total images, {1} rounds", numImageNodes, rounds);

                double startTime = UTCTime.Now();
                var ba = new BundleAdjuster(pipeline.Logger);
                ba.Adjust(scene, rounds, debugOutputFolder);
                pipeline.LogInfo("bundle adjust complete ({0:F3}s)", UTCTime.Now() - startTime);
                
                int n = 1;
                foreach (var adjNode in scene.Root.GetComponentsInTree<AdjustedNode>())
                {
                    pipeline.LogInfo("saving transform {0} of {1} adjusted frames", n++, numAdjustedNodes);
                    Microsoft.Xna.Framework.Matrix bundleResult = adjNode.Node.Transform.Matrix;
                    FrameTransform ft = FrameTransform.Find(pipeline, projectName, adjNode.Node.Name);
                    if (ft.Transform.Mean != bundleResult)
                    {
                        ft.Transform = new UncertainRigidTransform(bundleResult, ft.Transform.Distribution.Covariance); 
                    }
                    ft.Save(pipeline);
                }
            }
            else
            {
                pipeline.LogInfo("skipping bundle adjust of only {0} nodes", numAdjustedNodes);
            }

            return scene;
        }
    }
}
