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
            Dictionary<int, SceneNode> siteToNode = new Dictionary<int, SceneNode>();
            Dictionary<string, SceneNode> siteDriveToNode = new Dictionary<string, SceneNode>();
            AlignmentScene scene = new AlignmentScene();

            MSLLocations locations = new MSLLocations();

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

                var md = imgRef.Image.Metadata as PDSMetadata;
                if (md == null)
                {
                    continue;
                }
                images.Add(imgRef);

                PDSParser parsed = new PDSParser(md);
                // Create nodes
                {
                    if (!siteToNode.ContainsKey(parsed.Site))
                    {
                        siteToNode[parsed.Site] = new SceneNode("Site " + parsed.Site.ToString(), scene.Root.Transform);
                        var siteLoc = locations.Location(new SiteDrive(parsed.Site, 0));
                        if (siteLoc != null)
                        {
                            siteToNode[parsed.Site].Transform.LocalToWorld = Matrix.CreateTranslation(siteLoc.Position);
                        }
                    }
                    var siteNode = siteToNode[parsed.Site];
                    if (!siteDriveToNode.ContainsKey(parsed.SiteDrive))
                    {
                        siteDriveToNode[parsed.SiteDrive] = new SceneNode("Drive " + parsed.Drive.ToString(), siteNode.Transform);
                        var locData = locations.Location(new SiteDrive(parsed.Site, parsed.Drive));
                        siteDriveToNode[parsed.SiteDrive].Transform.LocalToWorld = Matrix.CreateTranslation(locData.Position);
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
            logger.Info("\n"+scene.DebugString());

            logger.Info("Detecting features...");
            Dictionary<ImageRef, ImageFeature[]> imageToFeatures = new Dictionary<ImageRef, ImageFeature[]>();
            // Detect all features
            string gpcafile = PCAKeypointProjector.DefaultTrainingSpace;
            PCAKeypointProjector projector = new PCAKeypointProjector(gpcafile, false);
            Parallel.ForEach(images, imgRef =>
            {
                List<PCASIFTFeature> features = new PCASIFTDetector(numFeatures: 10000).Detect(imgRef.Image, null).Cast<PCASIFTFeature>().ToList();
                PCAKeypointProjector proj;
                lock (projector)
                {
                    proj = projector.Clone();
                }
                proj.Project(imgRef.Image, features, 1);
                var arr = features.ToArray();
                lock (imageToFeatures)
                {
                    imageToFeatures[imgRef] = arr;
                }
            });
            logger.Info("Done.");

            logger.Info("Detecting overlaps...");
            FrustumOverlapDetector od = new FrustumOverlapDetector();
            od.Detect(scene);
            logger.Info("Done.");

            logger.Info("Candidate image pairs:");
            foreach (var pair in scene.Overlaps)
            {
                logger.InfoFormat("{0} -> {1}", pair.Key.DisplayName, pair.Value.DisplayName);
            }

            logger.Info("Finding correspondences...");
            Func<ImageRef, ImageRef, string> pairName = (img0, img1) =>
            {
                List<string> parts = new List<string> { img0.DisplayName, img1.DisplayName };
                parts.Sort();
                return parts[0] + "-" + parts[1];
            };
            Dictionary<string, ImagePairCorrespondence> corr = new Dictionary<string, ImagePairCorrespondence>();
            Serial.ForEach(scene.Overlaps, pair =>
            {
                var model = pair.Key;
                var data = pair.Value;
                if (corr.ContainsKey(pairName(model, data))) return;

                var modelFeat = imageToFeatures[model];
                var dataFeat = imageToFeatures[data];
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

                MatchImage.WriteMatchImage(matches, modelFeat, dataFeat, "D:\\match_0_bf.png");

                // KGF
                {
                    var kgf = new KnownGeometryFilter(new KnownGeometryFilter.ImageNodeDelegate(imgRef => scene.ImageToNode[imgRef]));
                    matches = kgf.Filter(matches, modelFeat, dataFeat);
                    if (matches == null || matches.DataToModel.Length < 8)
                    {
                        lock (logger)
                        {
                            logger.Debug("No matches for " + pairName(model, data));
                        }
                        return;
                    }
                }
                MatchImage.WriteMatchImage(matches, modelFeat, dataFeat, "D:\\match_1_kgf.png");

                // GTM
                {
                    var gtm = new GTM();
                    matches = gtm.Filter(matches, modelFeat, dataFeat);
                    if (matches == null || matches.DataToModel.Length < 8)
                    {
                        lock (logger)
                        {
                            logger.Debug("No matches for " + pairName(model, data));
                        }
                        return;
                    }
                }
                MatchImage.WriteMatchImage(matches, modelFeat, dataFeat, "D:\\match_2_gtm.png");

                // Moisan-Stival
                {
                    var ms = new MoisanStivalFilter();
                    matches = ms.Filter(matches, modelFeat, dataFeat);
                    if (matches == null || matches.DataToModel.Length < 8)
                    {
                        lock (logger)
                        {
                            logger.Debug("No matches for " + pairName(model, data));
                        }
                        return;
                    }
                }
                MatchImage.WriteMatchImage(matches, modelFeat, dataFeat, "D:\\match_3_ms.png");

                corr[pairName(model, data)] = matches;
                lock (logger)
                {
                    logger.DebugFormat("{0} matches for {1}", matches.DataToModel.Length, pairName(model, data));
                }
            });

            return 0;
        }
    }

}