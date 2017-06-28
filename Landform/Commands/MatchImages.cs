using CommandLine;
using System.Collections.Generic;
using System.Linq;
using OPS.Imaging.Emgu;
using OPS.Alignment;
using System.Diagnostics;
using Emgu.CV;
using Emgu.CV.Util;
using Emgu.CV.Structure;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;


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

            if (trainingPath != null)
            {
                Train(trainingFile, trainingPath);
                return 0;
            }

            if (outputFile == null)
            {
                outputFile = imageFileA.Substring(0, imageFileA.Length - 4) + ".jpg";
            }
            Imaging.Image model = Imaging.Image.Load(imageFileA);
            Imaging.Image data = Imaging.Image.Load(imageFileB);

            if (SIFTbool != null)
            {
                SIFT(model, data, outputFile);
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

        void DetectAndMatch(Imaging.Image model, Imaging.Image data, string gpcafile, string outputFile)
        {
            Trace.WriteLine("Matching images with PCA-SIFT...");
            List<PCASIFTFeature> featuresA = new PCASIFTDetector().Detect(model, null).Cast<PCASIFTFeature>().ToList();
            List<PCASIFTFeature> featuresB = new PCASIFTDetector().Detect(data, null).Cast<PCASIFTFeature>().ToList();
            PCAKeypointProjector projector = new PCAKeypointProjector(gpcafile, false);

            projector.Project(model, featuresA, 1);
            projector.Project(data, featuresB, 2);

            if (outputFile == null) { outputFile = options.TrainingFile + ".png"; }

            EmguSIFTMatcher matcher = new EmguSIFTMatcher();
            ImagePairCorrespondence matches = matcher.Match(new ImageRef(model), new ImageRef(data), featuresA, featuresB);
            MoisanStivalFilter filter = new MoisanStivalFilter();
            matches = filter.Filter(matches);
            
            PCAMatch.Match(matches, outputFile);
            Trace.WriteLine(string.Format("Matched images written to {0}", outputFile));
        }

        public void SIFT(Imaging.Image model, Imaging.Image data, string outputFile)
        {
            if (outputFile == null) { outputFile = options.TrainingFile + ".png"; }
            Image<Gray, byte> imageModel = model.ToEmguGrayscale();
            Image<Gray, byte> imageData = data.ToEmguGrayscale();
            List<SIFTFeature> modelfeat = ASIFT.Detect(imageModel, null, false).Cast<SIFTFeature>().ToList();
            List<SIFTFeature> datafeat = ASIFT.Detect(imageData, null, false).Cast<SIFTFeature>().ToList();
            Matrix<float> descr0 = ToDescriptorMatrix(modelfeat);
            Matrix<float> descr1 = ToDescriptorMatrix(datafeat);
            VectorOfKeyPoint kp0 = ToVOKP(modelfeat);
            VectorOfKeyPoint kp1 = ToVOKP(datafeat);
            EmguSIFTMatcher matcher = new EmguSIFTMatcher();
            ImagePairCorrespondence matches = matcher.Match(new ImageRef(model), new ImageRef(data), modelfeat, datafeat);
            MoisanStivalFilter filter = new MoisanStivalFilter();
            matches = filter.Filter(matches);
            PCAMatch.Match(matches, outputFile);
            Trace.WriteLine(string.Format("Matched images written to {0}", outputFile));
        }

        void Train(string trainingFile, string trainingPath)
        {
            if (trainingFile == null) { trainingFile = trainingPath; }
            Trace.WriteLine("Training...");
            PCATrain train = new PCATrain(trainingFile);
            train.Train(trainingPath);
            Trace.WriteLine("Trained.");
        }

        public static Matrix<float> ToDescriptorMatrix(List<SIFTFeature> features)
        {
            Matrix<float> res = new Matrix<float>(features.Count, features[0].Descriptor.Length);
            float[,] data = res.Data;
            int i, j;
            for (i = 0; i < features.Count; i++)
            {
                var d = ((FeatureDescriptor<float>)features[i].Descriptor).Data;
                for (j = 0; j < d.Length; j++)
                {
                    data[i, j] = d[j];
                }
            }
            return res;
        }

        static VectorOfKeyPoint ToVOKP(List<SIFTFeature> kps)
        {
            VectorOfKeyPoint res = new VectorOfKeyPoint();
            res.Push(kps.Select(kp =>
            {
                MKeyPoint _kp = new MKeyPoint();
                _kp.Size = (float)kp.Size;
                _kp.Point = new PointF((float)kp.Location.X, (float)kp.Location.Y);
                _kp.Angle = (float)kp.Angle;
                return _kp;
            }).ToArray());
            return res;
        }
    }
}
