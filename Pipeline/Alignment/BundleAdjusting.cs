using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.IO;
using log4net;
using Microsoft.Xna.Framework;
using MathNet.Numerics.LinearAlgebra;
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
                                                  int rounds = 2,
                                                  string debugOutputFolder = null,
                                                  Func<Observation, bool> observationFilter = null)
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
                if (siteDriveNode.IsLeaf)
                {
                    throw new Exception("site drive node should not be a leaf: " + siteDriveNode.Name);
                }
                if (siteDriveNode.HasComponent<NodeImage>())
                {
                    throw new Exception("site drive node should not have image component: " + siteDriveNode.Name);
                }
                if (adjustAcrossSiteDrives)
                {
                    siteDriveNode.AddComponent<AdjustedNode>();
                    numAdjustedNodes++;
                    nsd++;
                }
                foreach (var observationNode in siteDriveNode.Children)
                {
                    if (!observationNode.IsLeaf)
                    {
                        throw new Exception("observation node should be a leaf: " + observationNode.Name);
                    }
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
                var result = ba.Adjust(scene, rounds);
                pipeline.LogInfo("bundle adjust complete ({0:F3}s)", UTCTime.Now() - startTime);

                if (debugOutputFolder != null)
                {
                    Mesh m = new Mesh(capacity: result.Points.Count);
                    for (int i = 0; i < result.Points.Count; i++)
                    {
                        m.Vertices.Add(new Vertex(result.Points[i].Position));
                    }
                    var path = Path.Combine(debugOutputFolder, "bundlecloud.ply");
                    pipeline.LogInfo("writing bundle adjust debug point cloud to {0}", path);
                    PathHelper.EnsureExists(debugOutputFolder);
                    m.Save(path);
                }
                
                int n = 1;
                foreach (var adjNode in scene.Root.GetComponentsInTree<AdjustedNode>())
                {
                    pipeline.LogInfo("saving transform {0} of {1} adjusted frames", n++, numAdjustedNodes);
                    var bundleResult = adjNode.Node.Transform.Matrix;
                    var frame = adjNode.Node.GetComponent<NodeFrame>().Frame;
                    //TODO propagate transform covariance out of ceres
                    //https://github.jpl.nasa.gov/OnSight/Landform/issues/367
                    var ut = new UncertainRigidTransform(bundleResult);
                    FrameTransform ft = FrameTransform.FindOrCreate(pipeline, frame, TransformSource.Landform, ut);
                    ft.Transform = ut;
                    ft.Save(pipeline);
                    if (frame.AddTransform(ft))
                    {
                        frame.Save(pipeline);
                    }
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
