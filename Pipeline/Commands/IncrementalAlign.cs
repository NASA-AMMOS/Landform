using CommandLine;
using Emgu.CV.Util;
using log4net;
using MathNet.Numerics.LinearAlgebra;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using OPS.Alignment;
using OPS.Geometry;
using OPS.Imaging;
using OPS.MathExtensions;
using OPS.Plumbing;
using OPS.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Pipeline
{
    [Verb("align-incremental", HelpText = "Create CloudFormation templates for all DynamoDB tables.")]
    public class IncrementalAlignOptions
    {
        [Value(0, Required = true, HelpText = "Path containing images")]
        public string InputPath { get; set; }
        
        [Value(1, Required = true, HelpText = "Output JSON")]
        public string OutputPath { get; set; }
    }

    public class IncrementalAlign
    {
        static ILog logger = LogManager.GetLogger(typeof(IncrementalAlign));

        public IncrementalAlignOptions options;
        public IncrementalAlign(IncrementalAlignOptions options)
        {
            this.options = options;
        }

        struct Jerry
        {
            public string Name;
            public Vector3 Position;
        }

        public int Run()
        {
            PipelineCore pipeline = new PipelineCore(enableDynamo: false);

            var files = Directory.EnumerateFiles(options.InputPath).ToArray();

            ASIFTDetector detector = new ASIFTDetector();
            var feats = new Memoizer<ImageRef, DetectedFeatures>(imgRef =>
            {
                var featurePath = Path.Combine(options.OutputPath, Path.GetFileName(((DiskImageRef)imgRef).Path) + ".feat");
                if (File.Exists(featurePath))
                {
                    return DataProduct.Load<DetectedFeatures>(File.ReadAllBytes(featurePath));
                }

                ImageFeature[] features;
                var img = pipeline.Load(imgRef);
                OPS.Imaging.Image mask = null;// masks[imgRef];
                lock (detector)
                {
                    try
                    {
                        features = detector.Detect(img, mask).ToArray();
                    }
                    catch (CvException ex)
                    {
                        logger.Error("failed to detect for " + imgRef.DisplayName, ex);
                        return null;
                    }
                }
                features = features.OrderByDescending(f => ((SIFTFeature)f).Response).Take(10000).ToArray();

                DetectedFeatures feat = new DetectedFeatures
                {
                    Features = features,
                    ObservationName = imgRef.DisplayName
                };
                File.WriteAllBytes(featurePath, feat.Serialize());
                return feat;
            });

            AlignmentScene scene = new AlignmentScene();
            scene.Context = new MatchingContext();
            Parallel.ForEach(files, f =>
            {
                var imgRef = new DiskImageRef(f);
                var img = pipeline.Load(imgRef);
                // haha trust me
                img.CameraModel = new HayabusaCameraModel(120.71 / 1000, 0, img.Width / 1024.0);

                var data = feats[imgRef].Features;
                lock (scene.Context.DetectedFeatures)
                {
                    scene.Context.DetectedFeatures[imgRef] = data;
                }
            });

            var priorsJson = JsonConvert.DeserializeObject<List<Jerry>>(File.ReadAllText("D:\\priors.json"));
            var priorsDict = new Dictionary<string, Jerry>();
            foreach (var j in priorsJson)
            {
                priorsDict[j.Name] = j;
            }
            var priors = new Memoizer<ImageRef, UncertainRigidTransform>(img =>
            {
                string name = Path.GetFileNameWithoutExtension(img.DisplayName);
                if (!priorsDict.ContainsKey(name)) return null;

                var p = priorsDict[name];
                var posDistrib = new GaussianND(p.Position, Matrix.Identity * 100);

                var rotMat = Matrix.CreateLookAt(p.Position, Vector3.Zero, Vector3.UnitY);
                var rot = Quaternion.CreateFromRotationMatrix(rotMat);
                var lookatDistrib = new GaussianND(new AxisAngleVector(rot).AxisAngle, Matrix.Identity * (1 * Math.PI / 180));
                var zRotDistrib = new GaussianND(CreateVector.Dense(new[] { 0.0 }), CreateMatrix.Dense(1, 1, Math.PI / 2));
                var rotHyperDist = GaussianND.IndependentJoint(lookatDistrib, zRotDistrib);
                var rotDistrib = UnscentedTransform.Transform(rotHyperDist, vec =>
                {
                    var r0 = new AxisAngleVector(vec.SubVector(0, 3).ToXna()).ToMatrix();
                    var theta = vec[3];
                    var final = r0 * new AxisAngleVector(0, 0, theta).ToMatrix();
                    return new AxisAngleVector(Quaternion.CreateFromRotationMatrix(final)).AxisAngle.ToMathNet();
                });

                return new UncertainRigidTransform(GaussianND.IndependentJoint(posDistrib, rotDistrib));
            });

            for (int i = 1; i < files.Length; i++)
            {
                var current = new DiskImageRef(files[i - 1]);
                var next = new DiskImageRef(files[i]);

                var f0 = feats[current];
                var f1 = feats[next];

                var match = DoCorrespondence(new UnorderedImagePair(current, next), scene, pipeline);
                if (match == null) continue;

                if (match.FundamentalMatrix != null)
                {
                    logger.Info("Computed fundamental matrix");
                    var transform = EpipolarTransformDecomposition.ExtractTransform(scene, match);
                    logger.Info("Estimated transform: " + transform.ToString());
                }
            }

            return 0;
        }


        static string ToJson(object obj)
        {
            JsonSerializer serializer = new JsonSerializer();
            serializer.TypeNameHandling = TypeNameHandling.Auto;

            StringWriter sw = new StringWriter();
            serializer.Serialize(sw, obj);
            return sw.ToString();
        }

        static T FromJson<T>(string json) where T : new()
        {
            T res = new T();
            JsonSerializer serializer = new JsonSerializer();
            serializer.TypeNameHandling = TypeNameHandling.Auto;
            StringReader sr = new StringReader(json);
            serializer.Populate(sr, res);
            return res;
        }

        static readonly int MIN_MATCHES = 20;
        private ImagePairCorrespondence DoCorrespondence(UnorderedImagePair pair, AlignmentScene scene, PipelineCore pipeline)
        {
            Func<ImageRef, ImageRef, string> pairName = (img0, img1) =>
            {
                List<string> parts = new List<string> { img0.DisplayName, img1.DisplayName };
                parts.Sort();
                return parts[0] + "-" + parts[1];
            };

            if (scene.Context.Correspondences.ContainsKey(pair)) return null;

            var matchName = Path.GetFileNameWithoutExtension(((DiskImageRef)pair.One).Path) + "_x_" + Path.GetFileNameWithoutExtension(((DiskImageRef)pair.Two).Path);
            var matchPath = Path.Combine(options.OutputPath, "matches", "json", matchName + ".json");
            var matchImagePath = Path.Combine(options.OutputPath, "matches", pairName(pair.One, pair.Two) + ".png");
            if (File.Exists(matchPath))
            {
                string jsonText = File.ReadAllText(matchPath);
                if (jsonText == "null")
                {
                    return null;
                }
                else if (File.Exists(matchImagePath))
                {
                    ImagePairCorrespondence match = FromJson<ImagePairCorrespondence>(jsonText);
                    scene.Context.Correspondences[pair] = match;
                    return null;
                }
            }

            // prepopulate with null so we can bail without worry
            File.WriteAllText(matchPath, "null");

            var filters = new List<IMatchFilter>();
            if (scene.ImageToNode.ContainsKey(pair.One) && scene.ImageToNode.ContainsKey(pair.Two))
            {
                filters.Add(new KnownGeometryFilter(pipeline, new KnownGeometryFilter.ImageNodeDelegate(imgRef => scene.ImageToNode[imgRef])));
            }
            filters.AddRange(new IMatchFilter[] { new GTMFilter(), new MoisanStivalFilter(pipeline) });

            var model = pair.One;
            var data = pair.Two;
            var modelFeat = scene.Context.DetectedFeatures[model];
            var dataFeat = scene.Context.DetectedFeatures[data];
            BruteForceMatcher bfm = new BruteForceMatcher();

            var matches = bfm.Match(model, data, modelFeat, dataFeat);
            if (matches == null || matches.DataToModel.Length < MIN_MATCHES)
            {
                logger.Debug("No matches for " + pairName(model, data));
                return null;
            }

            foreach (var filt in filters)
            {
                matches = filt.Filter(scene, matches);
                if (matches == null || matches.DataToModel.Length < MIN_MATCHES)
                {
                    logger.Debug("No matches for " + pairName(model, data));
                    return null;
                }
            }
            
            scene.Context.Correspondences[pair] = matches;
            File.WriteAllText(matchPath, ToJson(matches));
            MatchImage.WriteMatchImage(pipeline, matches, modelFeat, dataFeat, matchImagePath);

            logger.DebugFormat("{0} matches for {1}", matches.DataToModel.Length, pairName(model, data));

            return matches;
        }
    }
}
