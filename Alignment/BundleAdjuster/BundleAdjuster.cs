using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Alignment.BundleAdjusterStructures;
using OPS.Geometry;
using OPS.Imaging;
using Microsoft.Xna.Framework;
using OPS.Util;
using System.IO;
using System.Runtime.InteropServices;

namespace OPS.Alignment
{
    public class BundleAdjuster
    {
        public static void Adjust(AlignmentScene scene)
        {
            var rootToWorld = scene.Root.GetOrAddComponent<NodeUncertainTransform>().UncertainTransform;
            HashSet<ImageRef> images = new HashSet<ImageRef>();

            BundleAdjusterProblem prob = new BundleAdjusterProblem();
            Dictionary<Imaging.CameraModel, int> modelToIndex = new Dictionary<Imaging.CameraModel, int>();
            Dictionary<ImageRef, int> imageToCamera = new Dictionary<ImageRef, int>();
            Dictionary<ImageRef, HashSet<ImageFeature>> imageToFeatures = new Dictionary<ImageRef, HashSet<ImageFeature>>();
            Dictionary<int, int> cameraToTransform = new Dictionary<int, int>();
            Dictionary<ImageFeature, int> featureToPoint = new Dictionary<ImageFeature, int>();
            Dictionary<int, List<int>> pointToProjection = new Dictionary<int, List<int>>();

            // Create RigidTransform objects for each node to be adjusted
            List<AdjustedNode> toAdjust = scene.Root.GetComponentsInTree<AdjustedNode>().ToList();
            foreach (var n in toAdjust)
            {
                UncertainRigidTransform nodeToRoot = n.Node.GetOrAddComponent<NodeUncertainTransform>().LocalToWorld.TimesInverse(rootToWorld);
                int transformIdx = prob.AddTransform(nodeToRoot.Mean);
                if (nodeToRoot.Uncertain) prob.AddPrior(transformIdx, nodeToRoot);

                foreach (var imgRefRef in n.Node.GetComponentsInTree<NodeImageReference>())
                {
                    var imgRef = imgRefRef.Reference;
                    var model = imgRef.Image.CameraModel;
                    int modelIdx = imageToCamera[imgRef] = prob.AddCameraModel((CAHV)model);
                    cameraToTransform[modelIdx] = transformIdx;
                    images.Add(imgRef);
                }
            }

            // Grab clusters of corresponding features as points
            Dictionary<int, int> dedupeMap = new Dictionary<int, int>();
            foreach (var corr in scene.Correspondences)
            {
                if (!images.Contains(corr.ModelImage) || !images.Contains(corr.DataImage)) continue;

                if (!imageToFeatures.ContainsKey(corr.DataImage))
                {
                    imageToFeatures[corr.DataImage] = new HashSet<ImageFeature>();
                }
                if (!imageToFeatures.ContainsKey(corr.ModelImage))
                {
                    imageToFeatures[corr.ModelImage] = new HashSet<ImageFeature>();
                }

                foreach (var pair in corr.DataToModel)
                {
                    Func<ImageFeature, int> tryGetId = (feat) =>
                    {
                        if (featureToPoint.ContainsKey(feat))
                        {
                            var res = featureToPoint[feat];
                            while (dedupeMap.ContainsKey(res)) res = dedupeMap[res];
                        }
                        return -1;
                    };

                    /*var modelFeat = corr.ModelFeatures[pair.Key];
                    var dataFeat = corr.DataFeatures[pair.Value];
                    imageToFeatures[corr.ModelImage].Add(modelFeat);
                    imageToFeatures[corr.DataImage].Add(dataFeat);

                    int modelId = tryGetId(modelFeat);
                    int dataId = tryGetId(dataFeat);
                   
                    // 1. both not present yet
                    if (modelId < 0 && dataId < 0)
                    {
                        modelId = dataId = prob.AddPoint(new Vector3(0, 0, 0));
                    }
                    // 2. at least one has already been encountered
                    else
                    {
                        //2a. one or both has been encountered already, but only one unique id is present
                        if (modelId == dataId || modelId < 0 || dataId < 0)
                        {
                            int existingId = Math.Max(modelId, dataId);
                            modelId = existingId;
                            dataId = existingId;
                        }

                        //2b. both have different ids
                        else
                        {
                            int winner = Math.Min(modelId, dataId);
                            int loser = Math.Max(modelId, dataId);
                            dedupeMap[loser] = winner;
                            modelId = dataId = winner;
                        }
                    }

                    featureToPoint[modelFeat] = modelId;
                    featureToPoint[dataFeat] = dataId;*/
                }
            }

            // Deduplicate points
            List<ImageFeature> allFeatures = featureToPoint.Keys.ToList();
            Dictionary<int, int> oldToNewPoint = new Dictionary<int, int>();
            List<BundleAdjusterStructures.Point> newPoints = new List<Point>();
            foreach (var feat in allFeatures)
            {
                int pointId = featureToPoint[feat];
                while (dedupeMap.ContainsKey(pointId)) pointId = dedupeMap[pointId];
                featureToPoint[feat] = pointId;
                if (!oldToNewPoint.ContainsKey(pointId))
                {
                    oldToNewPoint[pointId] = newPoints.Count;
                    newPoints.Add(new Point());
                }
            }
            prob.Points = newPoints;

            // Add projections
            foreach (var img in images)
            {
                int cameraIdx = imageToCamera[img];
                int transformIdx = cameraToTransform[cameraIdx];
                foreach (var feat in imageToFeatures[img])
                {
                    int pointIdx = featureToPoint[feat];
                    int projId = prob.AddProjection(cameraIdx, transformIdx, pointIdx, feat.Location);
                }
            }


            // Run
            TemporaryFile.GetAndDelete(".bin", (inputFile) =>
            {
                // Serialize out problem definition
                using (var f = File.OpenWrite(inputFile))
                {
                    using (BinaryWriter bw = new BinaryWriter(f))
                    {
                        prob.Write(bw);
                    }
                }

                TemporaryFile.GetAndDelete(".bin", (outputFile) =>
                {
                    ProgramRunner pr = new ProgramRunner("CeresBundler.exe", "\"" + inputFile + "\" \"" + outputFile + "\"");
                    pr.Run();

                    using (var f = File.OpenRead(outputFile))
                    {
                        using (var br = new BinaryReader(f))
                        {
                            var res = BundleAdjusterProblem.Read(br);
                            prob.Transforms = res.Transforms;
                            prob.Points = res.Points;
                            prob.CameraModels = res.CameraModels;
                        }
                    }
                });
            });

            // TODO: Write back results to scene graph
            
        }
    }
}
