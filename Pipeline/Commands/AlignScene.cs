using CommandLine;
using log4net;
using OPS.Alignment;
using OPS.Geometry;
using OPS.Imaging;
using OPS.Pipeline;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using OPS.Util;
using MathNet.Numerics.LinearAlgebra;
using OPS.Plumbing;

namespace OPS.Pipeline
{

    [Verb("align-scene", HelpText = "Align a folder of images")]
    public class AlignSceneOptions
    {
        [Value(0, Required = true, HelpText = "Path containing images")]
        public string InputPath { get; set; }


        [Value(1, Required = true, HelpText = "Output JSON")]
        public string OutputPath { get; set; }
    }

    public class AlignScene
    {
        static ILog logger = LogManager.GetLogger(typeof(AlignScene));

        AlignSceneOptions options;

        public AlignScene(AlignSceneOptions options)
        {
            this.options = options;
        }

        public int Run()
        {
            List<ImageRef> images = new List<ImageRef>();
            Dictionary<string, SceneNode> siteDriveToNode = new Dictionary<string, SceneNode>();
            AlignmentScene scene = new AlignmentScene();

            MSLLocations locations = new MSLLocations();

            PipelineCore pipeline = new PipelineCore();

            logger.Info("Building scene...");
            foreach (var fn in Directory.EnumerateFiles(options.InputPath))
            {
                if (Path.GetExtension(fn).ToUpper() != ".IMG")
                {
                    continue;
                }
                string path = Path.Combine(options.InputPath, fn);

                var imgRef = new DiskImageRef(path);

                logger.InfoFormat("{0}: {1}", imgRef.DisplayName, path);

                var md = imgRef.Load(pipeline).Metadata as PDSMetadata;
                if (md == null)
                {
                    continue;
                }
                images.Add(imgRef);

                PDSParser parsed = new PDSParser(md);
                // Create nodes
                {
                    if (!siteDriveToNode.ContainsKey(parsed.SiteDrive))
                    {
                        var res = siteDriveToNode[parsed.SiteDrive] = new SceneNode("SD " + parsed.SiteDrive, scene.Root.Transform);
                        var locData = locations.Location(new SiteDrive(parsed.Site, parsed.Drive));
                        res.Transform.LocalToWorld = Matrix.CreateTranslation(locData.Position);

                        // Made up covariance values, more or less signifying SD of 0.5m translation and 0.5deg rotation (1.0 for Z)
                        var uncertainty = res.AddComponent<NodeUncertainTransform>();
                        double halfDegSqr = Math.Pow(0.5 * Math.PI / 180, 2);
                        double degSqr = Math.Pow(1.0 * Math.PI / 180, 2);
                        uncertainty.Covariance = CreateMatrix.Diagonal(new double[] { 0.25, 0.25, 0.25, halfDegSqr, halfDegSqr, degSqr });
                    }
                }
                // Add image node
                var imgNode = new SceneNode(Path.GetFileNameWithoutExtension(imgRef.Path), siteDriveToNode[parsed.SiteDrive].Transform);
                imgNode.AddComponent<NodeImageReference>().Reference = imgRef;
                scene.ImageToNode[imgRef] = imgNode;
                imgNode.Transform.Rotation = parsed.RoverOriginRotation;
                imgNode.Transform.Translation = Vector3.Zero;
            }
            logger.Info("Done.");
            logger.Info("\n" + scene.DebugString());

            logger.Info("Detecting features...");
            Dictionary<ImageRef, ImageFeature[]> imageToFeatures = new Dictionary<ImageRef, ImageFeature[]>();
            // Detect all features
            string gpcafile = PCAKeypointProjector.DefaultTrainingSpace;
            PCAKeypointProjector projector = new PCAKeypointProjector(gpcafile, false);
            Parallel.ForEach(images, imgRef =>
            {
                if (imgRef is DiskImageRef)
                {
                    var path = ((DiskImageRef)imgRef).Path + ".feat";
                    if (File.Exists(path))
                    {
                        DetectedFeatures feat = DataProduct.Load<DetectedFeatures>(File.ReadAllBytes(path));
                        scene.Context.DetectedFeatures[imgRef] = feat.Features;
                        return;
                    }
                }

                List<PCASIFTFeature> features = new PCASIFTDetector(numFeatures: 10000).Detect(imgRef.Load(pipeline), null).Cast<PCASIFTFeature>().ToList();
                PCAKeypointProjector proj;
                lock (projector)
                {
                    proj = projector.Clone();
                }
                proj.Project(imgRef.Load(pipeline), features, 1);
                var arr = features.ToArray();
                if (imgRef is DiskImageRef)
                {
                    var path = ((DiskImageRef)imgRef).Path + ".feat";
                    DetectedFeatures feat = new DetectedFeatures();
                    feat.Features = arr;
                    feat.ObservationName = imgRef.DisplayName;
                    File.WriteAllBytes(path, feat.Serialize());
                }
                lock (scene.Context.DetectedFeatures)
                {
                    scene.Context.DetectedFeatures[imgRef] = arr;
                }
            });
            logger.Info("Done.");

            logger.Info("Detecting overlaps...");
            FrustumOverlapDetector od = new FrustumOverlapDetector(pipeline);
            od.Detect(scene);
            logger.Info("Done.");

            logger.Info("Candidate image pairs:");
            foreach (var pair in scene.Context.Overlaps)
            {
                logger.InfoFormat("{0} -> {1}", pair.One.DisplayName, pair.Two.DisplayName);
            }

            logger.Info("Finding correspondences...");
            Func<ImageRef, ImageRef, string> pairName = (img0, img1) =>
            {
                List<string> parts = new List<string> { img0.DisplayName, img1.DisplayName };
                parts.Sort();
                return parts[0] + "-" + parts[1];
            };
            Serial.ForEach(scene.Context.Overlaps, pair =>
            {
                if (scene.Context.Correspondences.ContainsKey(pair)) return;

                var model = pair.One;
                var data = pair.Two;
                var modelFeat = scene.Context.DetectedFeatures[model];
                var dataFeat = scene.Context.DetectedFeatures[data];
                BruteForceMatcher bfm = new BruteForceMatcher();

                var matches = bfm.Match(model, data, modelFeat, dataFeat);
                if (matches == null || matches.DataToModel.Length < 8)
                {
                    lock (logger)
                    {
                        logger.Debug("No matches for " + pairName(model, data));
                    }
                    return;
                }

                // KGF
                {
                    var kgf = new KnownGeometryFilter(pipeline, new KnownGeometryFilter.ImageNodeDelegate(imgRef => scene.ImageToNode[imgRef]));
                    matches = kgf.Filter(scene.Context, matches);
                    if (matches == null || matches.DataToModel.Length < 8)
                    {
                        lock (logger)
                        {
                            logger.Debug("No matches for " + pairName(model, data));
                        }
                        return;
                    }
                }

                // GTM
                try
                {
                    var gtm = new GTM();
                    matches = gtm.Filter(scene.Context, matches);
                    if (matches == null || matches.DataToModel.Length < 8)
                    {
                        lock (logger)
                        {
                            logger.Debug("No matches for " + pairName(model, data));
                        }
                        return;
                    }
                }
                catch (Exception ex)
                {
                    logger.Error("GTM failed", ex);
                    return;
                }

                // Moisan-Stival
                {
                    var ms = new MoisanStivalFilter(pipeline);
                    matches = ms.Filter(scene.Context, matches);
                    if (matches == null || matches.DataToModel.Length < 8)
                    {
                        lock (logger)
                        {
                            logger.Debug("No matches for " + pairName(model, data));
                        }
                        return;
                    }
                }

                scene.Context.Correspondences[pair] = matches;
                MatchImage.WriteMatchImage(pipeline, matches, modelFeat, dataFeat, Path.Combine(options.InputPath, "Matches", pairName(model, data) + ".png"));
                lock (logger)
                {
                    logger.DebugFormat("{0} matches for {1}", matches.DataToModel.Length, pairName(model, data));
                }
            });

            logger.Info("Bundle adjusting...");
            foreach (var kvp in siteDriveToNode)
            {
                kvp.Value.AddComponent<AdjustedNode>();
            }
            BundleAdjuster ba = new BundleAdjuster(pipeline);
            ba.Adjust(scene);
            logger.Info("Done.");

            return 0;
        }
    }

}