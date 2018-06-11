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
                OPS.Imaging.Image mask = new Image(1, img.Width, img.Height);
                for (int x = 0; x < img.Width; x++)
                {
                    for (int y = 0; y < img.Height; y++)
                    {
                        mask[0, y, x] = (img[0, y, x] > 20 / 256.0) ? 1 : 0;
                    }
                }
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
            Parallel.ForEach(files, f =>
            {
                var imgRef = new DiskImageRef(f);
                var img = pipeline.Load(imgRef);
                // haha trust me
                img.CameraModel = new HayabusaCameraModel(120.71 / 1000, 0, img.Width / 1024.0); // -2.8e-5

                var data = feats[imgRef].Features;
                lock (scene.DetectedFeatures)
                {
                    scene.DetectedFeatures[imgRef] = data;
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

            for (int i = 0; i < files.Length; i++)
            {
                var imgRef = new DiskImageRef(files[i]);
                var imgNode = new SceneNode(imgRef.DisplayName, scene.Root.Transform);
                imgNode.AddComponent<NodeImageReference>().Reference = imgRef;
                imgNode.GetOrAddComponent<NodeUncertainTransform>().UncertainTransform = priors[imgRef];
                scene.ImageToNode[imgRef] = imgNode;
            }

            Func<ImageRef, Matrix> intrinsicMat = (imgRef) =>
            {
                var img = pipeline.Load(imgRef);
                Matrix res = new Matrix();
                res[0, 0] = res[1, 1] = ((120.71 / 1000) * (1024.0 / img.Width));
                res[2, 0] = img.Width / 2;
                res[2, 1] = img.Height / 2;
                res[2, 2] = 1;
                res[3, 3] = 1;
                return res;
            };

            int K = 3;
            for (int i = 0; i < files.Length; i++)
            {
                var model = new DiskImageRef(files[i]);
                for (int j = i - K; j < i + K; j++)
                {
                    if (j < 0 || j >= files.Length) continue;
                    var data = new DiskImageRef(files[j]);

                    var f0 = feats[model];
                    var f1 = feats[data];

                    var match = DoCorrespondence(new UnorderedImagePair(model, data), scene, pipeline);
                    if (match == null) continue;

                    var pd = match.DataToModel.Select(d2m => feats[match.DataImage].Features[d2m.Key].Location).ToArray();
                    var pm = match.DataToModel.Select(d2m => feats[match.ModelImage].Features[d2m.Value].Location).ToArray();

                    if (match.BestTransformEstimate != null)
                    {
                        /*var F = match.FundamentalMatrix.matrix.ToMathNet(dimension: 3).Transpose();
                        var Km = intrinsicMat(model).ToMathNet(dimension: 3).Transpose();
                        var Kd = intrinsicMat(data).ToMathNet(dimension: 3).Transpose();
                        var e = Kd.Transpose() * F * Km;
                        //var ep = intrinsicMat(model) * match.FundamentalMatrix.matrix * Matrix.Transpose(intrinsicMat(data));
                        var E = new EpipolarMatrix(e.Transpose().ToXna());
                        var transform = EpipolarTransformDecomposition.ExtractTransform(scene, match, match.FundamentalMatrix);
                        logger.Info("Estimated transform: " + transform.ToString());

                        var justRot = transform.ToMathNet(dimension: 3).ToXna();
                        var reE = Matrix.Transpose(Matrix.Transpose(justRot) * CrossProductMatrix(transform.Translation));*/

                        var transform = match.BestTransformEstimate.Value;

                        var fakeScene = new AlignmentScene();
                        fakeScene.DetectedFeatures[model] = scene.DetectedFeatures[model];
                        fakeScene.DetectedFeatures[data] = scene.DetectedFeatures[data];
                        var mN = new SceneNode("model", fakeScene.Root.Transform);
                        mN.AddComponent<NodeImageReference>().Reference = model;
                        mN.Transform.Matrix = transform;
                        mN.Transform.Translation *= (priors[model].Mean.Translation - priors[data].Mean.Translation).Length() / transform.Translation.Length();
                        fakeScene.ImageToNode[model] = mN;
                        var dN = new SceneNode("data", fakeScene.Root.Transform);
                        dN.AddComponent<NodeImageReference>().Reference = data;
                        fakeScene.ImageToNode[data] = dN;
                        fakeScene.Correspondences[new UnorderedImagePair(model, data)] = match;

                        new BundleAdjuster(pipeline).Adjust(fakeScene);
                    }

                }
            }


            return 0;
        }
        
        static Matrix CrossProductMatrix(Vector3 vec)
        {
            var res = new Matrix();
            res[0, 1] = -vec.Z;
            res[0, 2] = vec.Y;
            res[1, 0] = vec.Z;
            res[1, 2] = -vec.X;
            res[2, 0] = -vec.Y;
            res[2, 1] = vec.X;
            res[3, 3] = 1;
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

            if (scene.Correspondences.ContainsKey(pair)) return null;

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
                    scene.Correspondences[pair] = match;
                    return null;
                }
            }

            // prepopulate with null so we can bail without worry
            File.WriteAllText(matchPath, "null");

            var filters = new List<IMatchFilter>();
            if (false && scene.ImageToNode.ContainsKey(pair.One) && scene.ImageToNode.ContainsKey(pair.Two))
            {
                var kgf = new KnownGeometryFilter(pipeline, new KnownGeometryFilter.ImageNodeDelegate(imgRef => scene.ImageToNode[imgRef]))
                {
                    MajorAxisThreshold = 10000
                };
                filters.Add(kgf);
            }
            filters.AddRange(new IMatchFilter[] { new GTMFilter(), new MoisanStivalFilter(pipeline) });

            var model = pair.One;
            var data = pair.Two;
            var modelFeat = scene.DetectedFeatures[model];
            var dataFeat = scene.DetectedFeatures[data];
            BruteForceMatcher bfm = new BruteForceMatcher();

            var matches = bfm.Match(model, data, modelFeat, dataFeat);
            if (matches == null || matches.DataToModel.Length < MIN_MATCHES)
            {
                logger.Debug("No matches for " + pairName(model, data));
                return null;
            }

            foreach (var filt in filters)
            {
                var initial = matches.DataToModel.Length;
                matches = filt.Filter(scene, matches);
                var left = (matches != null) ? matches.DataToModel.Length : 0;
                logger.DebugFormat("{0}: {1} -> {2}", filt.GetType().Name, initial, left);
                if (matches == null || matches.DataToModel.Length < MIN_MATCHES)
                {
                    logger.Debug("No matches for " + pairName(model, data));
                    return null;
                }
            }

            //var bestTransform = EpipolarTransformDecomposition.ExtractTransform(scene, matches, matches.FundamentalMatrix);

            
            scene.Correspondences[pair] = matches;
            File.WriteAllText(matchPath, ToJson(matches));
            MatchImage.WriteMatchImage(pipeline, matches, modelFeat, dataFeat, matchImagePath);

            logger.DebugFormat("{0} matches for {1}", matches.DataToModel.Length, pairName(model, data));

            return matches;
        }
    }
}
