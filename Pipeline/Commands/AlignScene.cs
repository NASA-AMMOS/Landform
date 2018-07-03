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
using System.Collections.Concurrent;
using Newtonsoft.Json;
using Emgu.CV.Util;

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

        Mesh Project(PDSParser parser, Image color, Image depth, Image mask)
        {
            Mesh res = new Mesh(hasColors: true);
            for (int j = 0; j < depth.Height; j++)
            {
                for (int i = 0; i < depth.Width; i++)
                {
                    if (mask[0, j, i] == 0) continue;

                    float d = depth[0, j, i];
                    if (float.IsNaN(d) || float.IsInfinity(d) || d < 0.001) continue;

                    Vector3 pt = depth.CameraModel.Unproject(new Vector2(i, j), d);
                    if (double.IsNaN(pt.X) || double.IsInfinity(pt.X)) continue;
                    float[] col = color.GetBandValues(j, i);
                    if (col.Length == 1)
                    {
                        col = new float[] { col[0], col[0], col[0] };
                    }

                    res.Vertices.Add(new Vertex(pt, Vector3.Zero, new Vector4(col[0], col[1], col[2], 1), Vector2.Zero));
                }
            }
            return res;
        }

        static string ToJson(object obj)
        {
            JsonSerializer serializer = new JsonSerializer();
            serializer.TypeNameHandling = TypeNameHandling.Auto;

            StringWriter sw = new StringWriter();
            serializer.Serialize(sw, obj);
            return sw.ToString();
        }

        static T FromJson<T>(string json) where T: new()
        {
            T res = new T();
            JsonSerializer serializer = new JsonSerializer();
            serializer.TypeNameHandling = TypeNameHandling.Auto;
            StringReader sr = new StringReader(json);
            serializer.Populate(sr, res);
            return res;
        }

        public int Run()
        {
            List<ImageRef> images = new List<ImageRef>();
            Dictionary<string, SceneNode> siteDriveToNode = new Dictionary<string, SceneNode>();
            AlignmentScene scene = new AlignmentScene();

            MSLLocations locations = new MSLLocations();

            PipelineCore pipeline = new PipelineCore();

            ConcurrentDictionary<ImageRef, Image> masks = new ConcurrentDictionary<ImageRef, Image>();
            Memoizer<ImageRef, Image> imageCache = new Memoizer<ImageRef, Image>(pipeline.Load);

            logger.Info("Building scene...");
            logger.Info("1. masks");
            Parallel.ForEach(Directory.EnumerateFiles(options.InputPath), fn =>
            {
                if (Path.GetExtension(fn).ToUpper() != ".IMG")
                {
                    return;
                }
                string path = Path.Combine(options.InputPath, fn);

                if (Path.GetFileNameWithoutExtension(fn).StartsWith("ML_")) return;

                var imgRef = new DiskImageRef(path);

                logger.InfoFormat("{0}: {1}", imgRef.DisplayName, path);

                var img = imageCache[imgRef];
                var md = img.Metadata as PDSMetadata;
                if (md == null)
                {
                    return;
                }
                PDSParser parsed;
                try
                {
                    parsed = new PDSParser(md);
                    var junk = parsed.Articulation;
                }
                catch (Exception ex)
                {
                    logger.Error("Failed to parse metadata", ex);
                    return;
                }
                if (parsed.ProductId.ProductType != RoverProductType.Image)
                {
                    return;
                }
                lock (images)
                {
                    images.Add(imgRef);
                }


                Image mask;
                var maskPath = Path.Combine(options.OutputPath, Path.GetFileNameWithoutExtension(fn) + ".mask.png");
                if (File.Exists(maskPath))
                {
                    mask = Image.Load(maskPath);
                }
                else
                {
                    mask = RoverMask.Build(img);
                    mask.Save<byte>(maskPath);
                }
                if (!masks.TryAdd(imgRef, mask))
                {
                    throw new Exception("couldn't add mask to dict");
                }
            });

            logger.Info("2. scene graph");
            foreach (var imgRef in images)
            {
                string fn = ((DiskImageRef)imgRef).Path;
                var md = imageCache[imgRef].Metadata as PDSMetadata;
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
                        double fifthDegSqr = Math.Pow(0.2 * Math.PI / 180, 2);
                        double tenCM = 0.1;
                        double posCov = tenCM * tenCM;
                        uncertainty.Covariance = CreateMatrix.Diagonal(new double[] { posCov, posCov, posCov, fifthDegSqr, fifthDegSqr, fifthDegSqr });
                    }
                }
                // Add image node
                var imgNode = new SceneNode(Path.GetFileNameWithoutExtension(fn), siteDriveToNode[parsed.SiteDrive].Transform);
                imgNode.Transform.Rotation = parsed.RoverOriginRotation;
                imgNode.Transform.Translation = Vector3.Zero;
                {
                    var uncertainty = imgNode.AddComponent<NodeUncertainTransform>();
                    double fifthDegSqr = Math.Pow(0.2 * Math.PI / 180, 2);
                    double fiveMM = 0.005;
                    double posCov = fiveMM * fiveMM;
                    uncertainty.Covariance = CreateMatrix.Diagonal(new double[] { posCov, posCov, posCov, fifthDegSqr, fifthDegSqr, fifthDegSqr });
                }

                imgNode.AddComponent<NodeImageReference>().Reference = imgRef;
                scene.ImageToNode[imgRef] = imgNode;
            }

            logger.Info("3. point clouds");
            Parallel.ForEach(images, imgRef =>
            {
                var fn = ((DiskImageRef)imgRef).Path;
                var imgNode = scene.ImageToNode[imgRef];
                var md = imageCache[imgRef].Metadata as PDSMetadata;
                PDSParser parsed = new PDSParser(md);

                string colName = Path.GetFileName(fn);
                if (colName.Substring(13, 3) == "RAS")
                {
                    string depthName = colName.Substring(0, 13) + "RNG" + colName.Substring(16);
                    string depthPath = Path.Combine("E:\\TerrainSourceAssets\\xyzmore", depthName);
                    string cloudPath = Path.Combine(Path.GetDirectoryName(options.OutputPath), imgNode.Name + ".ply");

                    if (File.Exists(depthPath))
                    {
                        var rayC = imgNode.AddComponent<CameraRay>();
                        var ray = md.CameraModel.Unproject(new Vector2(md.Width / 2.0, md.Height / 2.0));
                        rayC.Center = ray.Position;
                        rayC.Direction = ray.Direction;

                        bool add = false;
                        if (!File.Exists(cloudPath))
                        {
                            Image depth = Image.Load(depthPath);
                            Mesh pc = Project(parsed, pipeline.Load(imgRef), depth, masks[imgRef]);
                            PLYSerializer.Write(pc, cloudPath, new PLYMaximumCompatibilityWriter(true));
                            add = pc.Vertices.Count > 1;
                        }
                        else if (new FileInfo(cloudPath).Length > 200) // approx length of PLY header
                        {
                            add = true;
                        }

                        if (add)
                        {
                            var pair = imgNode.AddComponent<PointCloudReference>();
                            pair.Path = cloudPath;
                        }
                    }
                }
            });
            logger.Info("4. serialize graph");
            scene.Save(options.OutputPath + ".pre.json");
            logger.Info("Done.");
            logger.Info("\n" + scene.DebugString());

            logger.Info("Detecting features...");
            Dictionary<ImageRef, ImageFeature[]> imageToFeatures = new Dictionary<ImageRef, ImageFeature[]>();
            // Detect all features
            string gpcafile = PCAKeypointProjector.DefaultTrainingSpace;
            PCAKeypointProjector projector = new PCAKeypointProjector(gpcafile, false);
            ASIFTDetector detector = new ASIFTDetector();

            bool asift = true;

            scene.DetectedFeatures = new Dictionary<ImageRef, ImageFeature[]>();
            Parallel.ForEach(images, imgRef =>
            {
                var featurePath = Path.Combine(options.OutputPath, Path.GetFileName(((DiskImageRef)imgRef).Path) + ".feat");
                if (File.Exists(featurePath))
                {
                    DetectedFeatures defeats = DataProduct.Load<DetectedFeatures>(File.ReadAllBytes(featurePath));
                    lock (scene.DetectedFeatures)
                    {
                        scene.DetectedFeatures[imgRef] = defeats.Features;
                    }
                    return;
                }

                ImageFeature[] features;
                var img = imageCache[imgRef];
                var mask = masks[imgRef];
                if (asift)
                {
                    lock (detector)
                    {
                        try
                        {

                            features = detector.Detect(img, mask).ToArray();
                        }
                        catch (CvException ex)
                        {
                            logger.Error("failed to detect for " + imgRef.DisplayName, ex);
                            return;
                        }
                    }
                    features = features.OrderByDescending(feat => ((SIFTFeature)feat).Response).Take(10000).ToArray();
                }
                else
                {
                    List<PCASIFTFeature> feats = new PCASIFTDetector(numFeatures: 10000).Detect(img, mask).Cast<PCASIFTFeature>().ToList();
                    PCAKeypointProjector proj;
                    lock (projector)
                    {
                        proj = projector.Clone();
                    }
                    proj.Project(imgRef.Load(pipeline), feats, 1);
                    features = feats.ToArray();
                }

                {
                    DetectedFeatures feat = new DetectedFeatures
                    {
                        Features = features,
                        ObservationName = imgRef.DisplayName
                    };
                    File.WriteAllBytes(featurePath, feat.Serialize());
                }
                lock (scene.DetectedFeatures)
                {
                    scene.DetectedFeatures[imgRef] = features;
                }
            });
            logger.Info("Done.");

            logger.Info("Detecting overlaps...");
            string overlapPath = Path.Combine(options.OutputPath, "overlaps.json");
            FrustumOverlapDetector od = new FrustumOverlapDetector(pipeline);
            if (File.Exists(overlapPath))
            {
                scene.Overlaps = new HashSet<UnorderedImagePair>(FromJson<List<UnorderedImagePair>>(File.ReadAllText(overlapPath)));
                od.MakeHulls(scene);
            }
            else
            {
                od.Detect(scene);
                File.WriteAllText(overlapPath, ToJson(scene.Overlaps.ToList()));
            }
            logger.Info("Done.");

            logger.Debug("Candidate image pairs:");
            foreach (var pair in scene.Overlaps)
            {
                logger.DebugFormat("{0} -> {1}", pair.One.DisplayName, pair.Two.DisplayName);
            }

            logger.Info("Finding correspondences...");
            Parallel.ForEach(scene.Overlaps, pair =>
            {
                DoCorrespondence(pair, scene, pipeline);
            });

            logger.Info("Bundle adjusting...");
            foreach (var kvp in siteDriveToNode)
            {
                kvp.Value.AddComponent<AdjustedNode>();
            }
            foreach (var kvp in scene.ImageToNode)
            {
                kvp.Value.AddComponent<AdjustedNode>();
            }
            BundleAdjuster ba = new BundleAdjuster(pipeline);
            ba.Adjust(scene);
            logger.Info("Done.");

            scene.Save(Path.Combine(options.OutputPath, "scene.json"));
            return 0;
        }

        private void DoCorrespondence(UnorderedImagePair pair, AlignmentScene scene, PipelineCore pipeline)
        {
            Func<ImageRef, ImageRef, string> pairName = (img0, img1) =>
            {
                List<string> parts = new List<string> { img0.DisplayName, img1.DisplayName };
                parts.Sort();
                return parts[0] + "-" + parts[1];
            };

            if (scene.Correspondences.ContainsKey(pair)) return;

            var matchName = Path.GetFileNameWithoutExtension(((DiskImageRef)pair.One).Path) + "_x_" + Path.GetFileNameWithoutExtension(((DiskImageRef)pair.Two).Path);
            var matchPath = Path.Combine(options.OutputPath, "matches", "json", matchName + ".json");
            var matchImagePath = Path.Combine(options.OutputPath, "matches", pairName(pair.One, pair.Two) + ".png");
            if (File.Exists(matchPath))
            {
                string jsonText = File.ReadAllText(matchPath);
                if (jsonText == "null")
                {
                    return;
                }
                else if (File.Exists(matchImagePath))
                {
                    ImagePairCorrespondence match = FromJson<ImagePairCorrespondence>(jsonText);
                    scene.Correspondences[pair] = match;
                    return;
                }
            }

            // prepopulate with null so we can bail without worry
            File.WriteAllText(matchPath, "null");

            var model = pair.One;
            var data = pair.Two;
            var modelFeat = scene.DetectedFeatures[model];
            var dataFeat = scene.DetectedFeatures[data];
            BruteForceMatcher bfm = new BruteForceMatcher();

            var matches = bfm.Match(model, data, modelFeat, dataFeat);
            if (matches == null || matches.DataToModel.Length < 20)
            {
                lock (logger)
                {
                    logger.Debug("No matches for " + pairName(model, data));
                }
                return;
            }


            // KGF
            if (true)
            {
                var kgf = new KnownGeometryFilter(pipeline, new KnownGeometryFilter.ImageNodeDelegate(imgRef => scene.ImageToNode[imgRef]));
                matches = kgf.Filter(scene, matches);
                if (matches == null || matches.DataToModel.Length < 20)
                {
                    lock (logger)
                    {
                        logger.Debug("No matches for " + pairName(model, data));
                    }
                    return;
                }
            }

            // GTM
            if (true)
            {
                try
                {
                    var gtm = new GTMFilter();
                    matches = gtm.Filter(scene, matches);
                    if (matches == null || matches.DataToModel.Length < 20)
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
            }

            // Moisan-Stival
            if (true)
            {
                var ms = new MoisanStivalFilter(pipeline);
                matches = ms.Filter(scene, matches);
                if (matches == null || matches.DataToModel.Length < 20)
                {
                    lock (logger)
                    {
                        logger.Debug("No matches for " + pairName(model, data));
                    }
                    return;
                }
            }

            scene.Correspondences[pair] = matches;
            File.WriteAllText(matchPath, ToJson(matches));
            MatchImage.WriteMatchImage(pipeline, matches, modelFeat, dataFeat, matchImagePath);
            lock (logger)
            {
                logger.DebugFormat("{0} matches for {1}", matches.DataToModel.Length, pairName(model, data));
            }
        }
    }

}