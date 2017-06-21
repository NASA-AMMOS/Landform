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

        [Option('p', "patches", Required = false, HelpText = "Indicate directory for gathering and saving patches")]
        public string PatchFile { get; set; }

        [Option('t', "train", Required = false, HelpText = "Indicate directory of training images")]
        public string TrainingPath { get; set; }

        [Option('o', "output", Required = false, HelpText = "Indicate output image file for image matches")]
        public string OutputFile { get; set; }

        [Option('e', "eigenspace", Required = false, HelpText = "Indicate saving location for computed eigenspace")]
        public string TrainingFile { get; set; }

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
            string patches = options.PatchFile;

            if (patches != null)
            {
                PrecomputePatches(patches);
                return -1;
            }


            if (trainingPath != null)
            {
                Train(trainingFile, trainingPath);
                return 0;
            }

            if (outputFile == null)
            {
                outputFile = imageFileA.Substring(0, imageFileA.Length-4) + ".jpg";
            }
            Imaging.Image model = Imaging.Image.Load(imageFileA);
            Imaging.Image data = Imaging.Image.Load(imageFileB);

            //SIFT(model, data, outputFile);

            Debug.WriteLine("Using images {0} and {1}", imageFileA, imageFileB);
            DetectAndMatch(model, data, gpcafile, outputFile);

            return 1;
        }

        void PrecomputePatches(string directory)
        {
            string[] imageFiles = Directory.GetFiles(directory, "*.png");

            List<PCA_SIFTFeature> features = new List<PCA_SIFTFeature>();
            PCA_SIFTDetector detector = new PCA_SIFTDetector();

            Parallel.For(0, imageFiles.Count(), i =>
            {
                string imageFile = imageFiles[i];
                Imaging.Image image = Imaging.Image.Load(imageFiles[i]);
                List<PCA_SIFTFeature> featuresA = detector.Detect(image, null).Cast<PCA_SIFTFeature>().ToList();
                features.AddRange(PCA_KeypointDetector.GetPatches(image.ToEmguGrayscale().Convert<Gray, float>(), featuresA, 41));
                PCA_KeypointDetector.WritePatchesToFile(featuresA, imageFile.Substring(0, imageFile.Length - 4) + ".patch", 41);
            });
        }


        void DetectAndMatch(Imaging.Image model, Imaging.Image data, string gpcafile, string outputFile)
        {
            Trace.WriteLine("Matching images with PCA-SIFT...");
            List<PCA_SIFTFeature> featuresA = new PCA_SIFTDetector().Detect(model, null).Cast<PCA_SIFTFeature>().ToList();
            List<PCA_SIFTFeature> featuresB = new PCA_SIFTDetector().Detect(data, null).Cast<PCA_SIFTFeature>().ToList();
            PCA_KeypointDetector detector = new PCA_KeypointDetector(gpcafile);

            detector.ProjectKeypoints(model, featuresA);
            detector.ProjectKeypoints(data, featuresB);

            if (outputFile == null) { outputFile = options.TrainingFile + ".png"; }

            EmguSIFTMatcher matcher = new EmguSIFTMatcher();
            ImagePairCorrespondence matches = matcher.Match(new ImageRef(model), new ImageRef(data), featuresA, featuresB);
            MoisanStivalFilter filter = new MoisanStivalFilter();
            matches = filter.Filter(matches);

            PCA_Match.Match(matches, outputFile);
            Trace.WriteLine("Images matched");
            //Matrix<float> descr0 = ToDescriptorMatrix(featuresA.Cast<SIFTFeature>().ToList());
            //Matrix<float> descr1 = ToDescriptorMatrix(featuresB.Cast<SIFTFeature>().ToList());
            //VectorOfKeyPoint kp0 = ToVOKP(featuresA.Cast<SIFTFeature>().ToList());
            //VectorOfKeyPoint kp1 = ToVOKP(featuresB.Cast<SIFTFeature>().ToList());
            ////EmguSIFTMatcher matcher = new EmguSIFTMatcher();
            ////ImagePairCorrespondence matches = matcher.Match(new ImageRef(model), new ImageRef(data), featuresA.Cast<SIFTFeature>().ToList(), featuresB.Cast<SIFTFeature>().ToList());
            ////MoisanStivalFilter filter = new MoisanStivalFilter();
            ////matches = filter.Filter(matches);
            ////PCA_Match.Match(matches, outputFile);
            //PCA_Match.Match(model.ToEmguGrayscale(), data.ToEmguGrayscale(), descr0, kp0, descr1, kp1, outputFile);
        }

        public void SIFT(Imaging.Image model, Imaging.Image data, string outputFile)
        {
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
            PCA_Match.Match(matches, outputFile);
            //PCA_Match.Match(model.ToEmguGrayscale(), data.ToEmguGrayscale(), descr0, kp0, descr1, kp1, outputFile);
        }

        void Train(string trainingFile, string trainingPath)
        {
            if (trainingFile == null) { trainingFile = trainingPath; }
            Trace.WriteLine("Training...");
            PCA_Train train = new PCA_Train(trainingFile);
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
