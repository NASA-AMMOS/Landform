using CommandLine;
using System.Collections.Generic;
using System.Linq;
using OPS.Imaging;
using OPS.Imaging.Emgu;
using OPS.Alignment;
using System.Diagnostics;
using Emgu.CV;
using Emgu.CV.Util;
using Emgu.CV.Structure;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using log4net;
using OPS.Geometry;
using System;
using Microsoft.Xna.Framework;
using MathNet.Numerics.LinearAlgebra;

namespace OPS.Pipeline
{

    [Verb("matchimages", HelpText = "")]
    public class MatchImagesOptions
    {
        [Value(0, Required = false, HelpText = "")]
        public string ImageA { get; set; }

        [Value(1, Required = false, HelpText = "")]
        public string ImageB { get; set; }

        [Option('t', "train", Required = false, HelpText = "Indicate directory of training images")]
        public string TrainingPath { get; set; }

        [Option('o', "output", Required = false, HelpText = "Indicate output image file for image matches")]
        public string OutputFile { get; set; }

        [Option('e', "eigenspace", Required = false, HelpText = "Indicate saving location for computed eigenspace")]
        public string TrainingFile { get; set; }

        [Option('s', "sift", Required = false, HelpText = "Use standard SIFT detection and description")]
        public string SIFTbool { get; set; }

    }

    public class MatchImages
    {
        public MatchImagesOptions options;

        private static readonly ILog logger = LogManager.GetLogger(typeof(MatchImages));

        public MatchImages(MatchImagesOptions options)
        {
            this.options = options;
        }

        public int Run()
        {
            string imageFileA = options.ImageA;
            string imageFileB = options.ImageB;
            string outputFile = options.OutputFile;
            string trainingPath = options.TrainingPath;
            string trainingFile = options.TrainingFile;
            string gpcafile = options.TrainingFile;
            string SIFTbool = options.SIFTbool;

            if (gpcafile == null)
            {
                gpcafile = PCAKeypointProjector.DefaultTrainingSpace;
            }

            if (trainingPath != null)
            {
                Train(trainingFile, trainingPath);
                return 0;
            }

            if (outputFile == null)
            {
                outputFile = imageFileA.Substring(0, imageFileA.Length - 4) + ".jpg";
            }

            ImageRef model = new ImageRef(imageFileA);
            ImageRef data = new ImageRef(imageFileB);

            if (SIFTbool != null)
            {
                SIFT(model.Image, data.Image, outputFile);
                return 2;
            }

            DetectAndMatch(model, data, gpcafile, outputFile);

            return 1;
        }

        void PrecomputePatches(string directory)
        {
            string[] imageFiles = Directory.GetFiles(directory, "*.png");

            List<PCASIFTFeature> features = new List<PCASIFTFeature>();
            PCASIFTDetector detector = new PCASIFTDetector();

            Parallel.For(0, imageFiles.Count(), i =>
            {
                string imageFile = imageFiles[i];
                Imaging.Image image = Imaging.Image.Load(imageFiles[i]);
                List<PCASIFTFeature> featuresA = detector.Detect(image, null).Cast<PCASIFTFeature>().ToList();
                features.AddRange(PCATrain.GetPatches(image.ToEmguGrayscale().Convert<Gray, float>(), featuresA, 41));
                PCASIFTIO.WritePatchesToFile(featuresA, imageFile.Substring(0, imageFile.Length - 4) + ".patch", 41);
            });
        }

        void DetectAndMatch(ImageRef modelRef, ImageRef dataRef, string gpcafile, string outputFile)
        {
            var model = modelRef.Image;
            var data = dataRef.Image;

            logger.Info("Matching images with PCA-SIFT...");
            Stopwatch watch = new Stopwatch();
            watch.Start();
            List<PCASIFTFeature> featuresA = new PCASIFTDetector().Detect(model, null).Cast<PCASIFTFeature>().ToList();
            List<PCASIFTFeature> featuresB = new PCASIFTDetector().Detect(data, null).Cast<PCASIFTFeature>().ToList();
            PCAKeypointProjector projector = new PCAKeypointProjector(gpcafile, false);

            var taskA = Task.Run(() =>
            {
                projector.Project(model, featuresA, 1);
            });
            var taskB = Task.Run(() =>
            {
                projector.Project(data, featuresB, 2);
            });

            taskA.Wait();
            taskB.Wait();

            if (outputFile == null) { outputFile = options.TrainingFile + ".png"; }

            BruteForceMatcher matcher = new BruteForceMatcher();
            ImagePairCorrespondence matches = matcher.Match(modelRef, dataRef, featuresA, featuresB);

            if (model.Metadata is PDSMetadata && data.Metadata is PDSMetadata)
            {
                logger.Info("Using KnownGeometryFilter");

                // Construct scene graph to feed to KnownGeometryFilter
                MSLLocations loc = new MSLLocations();
                SceneNode root = new SceneNode();
                Dictionary<ImageRef, SceneNode> nodes = new Dictionary<ImageRef, SceneNode>();
                
                Action<ImageRef> makeNode = (imgRef) =>
                {
                    var res = new SceneNode(imgRef.FilenameWithoutExtension, root.Transform);
                    PDSParser p = new PDSParser(imgRef.Metadata as PDSMetadata);
                    
                    var quat = p.RoverOriginRotation;
                    var siteLoc = loc.Location(new SiteDrive(p.SiteDrive));
                    Matrix toWorld = Matrix.CreateFromQuaternion(quat) * Matrix.CreateTranslation(siteLoc.Position);
                    res.Transform.Matrix = toWorld;

                    var chc = res.AddComponent<NodeConvexHull>();
                    chc.hull = ConvexHull.FromImage(imgRef.Image);

                    // Made up covariance values, more or less signifying SD of 0.5m translation and 0.5deg rotation (1.0 for Z)
                    var uncertainty = res.AddComponent<NodeUncertainTransform>();
                    double halfDegSqr = Math.Pow(0.5 * Math.PI / 180, 2);
                    double degSqr = Math.Pow(1.0 * Math.PI / 180, 2);
                    uncertainty.Covariance = CreateMatrix.Diagonal(new double[] { 0.25, 0.25, 0.25, halfDegSqr, halfDegSqr, degSqr });
                    nodes[imgRef] = res;
                };
                makeNode(modelRef);
                makeNode(dataRef);

                KnownGeometryFilter kg = new KnownGeometryFilter(imgRef => nodes[imgRef]);
                matches = kg.Filter(matches);
                if (matches == null)
                {
                    logger.Info("No matches found after KnownGeometryFilter");
                    return;
                }
                logger.Info(string.Format("{0} matches after KnownGeometryFilter", matches.DataToModel.Length));
            }
            else
            {
                logger.Info("Not using KnownGeometryFilter");
            }

            MoisanStivalFilter filter = new MoisanStivalFilter();
            matches = filter.Filter(matches);
            if (matches == null)
            {
                logger.Info("No matches found after MoisanStivalFilter");
                return;
            }
            logger.Info(string.Format("{0} matches after MoisanStivalFilter", matches.DataToModel.Length));
            GTM gtm = new GTM(5);
            matches = gtm.Filter(matches);
            if (matches == null)
            {
                logger.Info("No matches found after GTM Filter");
                return;
            }
            logger.Info(string.Format("{0} matches after GTM Filter", matches.DataToModel.Length));

            watch.Stop();
            MatchImage.WriteMatchImage(matches, outputFile, watch.ElapsedMilliseconds.ToString());
            logger.Info(string.Format("Matched images written to {0}", outputFile));
        }

        public void SIFT(Imaging.Image model, Imaging.Image data, string outputFile)
        {
            if (outputFile == null) { outputFile = options.TrainingFile + ".png"; }
            Image<Gray, byte> imageModel = model.ToEmguGrayscale();
            Image<Gray, byte> imageData = data.ToEmguGrayscale();

            ASIFTDetector asift = new ASIFTDetector();

            List<SIFTFeature> modelfeat = asift.Detect(imageModel, null).Cast<SIFTFeature>().ToList();
            List<SIFTFeature> datafeat = asift.Detect(imageData, null).Cast<SIFTFeature>().ToList();
            EmguSIFTMatcher matcher = new EmguSIFTMatcher();
            ImagePairCorrespondence matches = matcher.Match(new ImageRef(model), new ImageRef(data), modelfeat, datafeat);
            MoisanStivalFilter filter = new MoisanStivalFilter();
            matches = filter.Filter(matches);
            GTM gtm = new GTM(5);
            matches = gtm.Filter(matches);

            MatchImage.WriteMatchImage(matches, outputFile);
            logger.Info(string.Format("Matched images written to {0}", outputFile));
        }

        void Train(string trainingFile, string trainingPath)
        {
            if (trainingFile == null) { trainingFile = trainingPath; }
            logger.Info("Training...");
            PCATrain train = new PCATrain(trainingFile);
            train.Train(trainingPath);
            logger.Info("Trained.");
        }
    }
}
