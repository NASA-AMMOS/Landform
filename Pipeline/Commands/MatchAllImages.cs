using CommandLine;
using System.Collections.Generic;
using System.Linq;
using OPS.Imaging;
using OPS.Imaging.Emgu;
using System.Diagnostics;
using Emgu.CV;
using Emgu.CV.Util;
using Emgu.CV.Structure;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System;
using OPS.Util;
using OPS.Alignment;
namespace OPS.Pipeline
{

    [Verb("matchallimages", HelpText = "")]
    public class MatchAllImagesOptions
    {
        [Value(0, Required = true, HelpText = "Indicate directory of images to compare")]
        public string TrainingDirectory { get; set; }

        [Option('e', "eigenspace", Required = false, HelpText = "Indicate location for computed eigenspace")]
        public string Eigenspace { get; set; }

        [Option('s', "sift", Required = false, HelpText = "Use standard SIFT detection and description")]
        public string SIFTbool { get; set; }

        [Option('a', "analyze", Required = false, HelpText = "Compare various methods of image feature matching")]
        public string Analyze { get; set; }
    }

    public class MatchAllImages
    {
        public MatchAllImagesOptions options;
        public MatchAllImages(MatchAllImagesOptions options)
        {
            this.options = options;
        }

        enum MatchAlg
        {
            PCASIFT,
            ASIFT,
            //PCS
        };

        enum Filter
        {
            GTM,
            MoisanStival,
            MoisanStival_GTM
        };

        public int Run()
        {
            string directory = options.TrainingDirectory;
            string eigenspace = options.Eigenspace;
            bool SIFTbool = options.SIFTbool != null;

            if (eigenspace == null)
            {
                eigenspace = PCAKeypointProjector.DefaultTrainingSpace;
            }

            string[] imageFiles = Directory.GetFiles(directory, "*.png").Concat(Directory.GetFiles(directory, "*.jpg")).ToArray();

            ProcessImages(imageFiles, eigenspace, options.Analyze != null);

            return 1;
        }

        void ProcessImages(string[] images, string eigenspace, bool analyze = false)
        {
            if (analyze)
            {
                Parallel.ForEach(Enum.GetValues(typeof(MatchAlg)).Cast<MatchAlg>(), matchalg =>
                {
                    Parallel.ForEach(MatchImagePairs(images, matchalg, eigenspace), matches =>
                    {
                        if (matches.Correspondence == null) return;
                        foreach (Filter filter in Enum.GetValues(typeof(Filter)))
                        {
                            string outputFile = matches.OutputFile + "_" + filter.ToString() + ".png";
                            FilterAndSave(matches.Correspondence, filter, outputFile);
                        }
                    });
                });
            }
        }

        class Matches
        {
            public ImagePairCorrespondence Correspondence;
            public string OutputFile;

            public Matches(ImagePairCorrespondence matches, string outputfile)
            {
                Correspondence = matches;
                OutputFile = outputfile;
            }
        }

        IEnumerable<Matches> MatchImagePairs(string[] images, MatchAlg matchAlg, string eigenspace)
        {
            for (int i = 0; i < images.Length - 1; i++)
            {
                Imaging.Image model = Imaging.Image.Load(images[i]);
                for (int j = i + 1; j < images.Length; j++)
                {
                    Imaging.Image data = Imaging.Image.Load(images[j]);
                    string outputFile = Directory.CreateDirectory(Directory.GetParent(images[i]) 
                                        + "\\" + matchAlg + "\\").FullName
                                        + Path.GetFileNameWithoutExtension(images[i])
                                        + "_" + Path.GetFileNameWithoutExtension(images[j]);
                    yield return new Matches(DetectAndMatch(model, data, eigenspace, outputFile, matchAlg), outputFile);
                }
            }
        }

        ImagePairCorrespondence DetectAndMatch(Imaging.Image model, Imaging.Image data, string eigenspace, string outputFile, MatchAlg matchAlg)
        {
            ImagePairCorrespondence matches = null;
            if (matchAlg == MatchAlg.PCASIFT)
            {
                Trace.WriteLine("Matching images with PCA-SIFT...");
                List<PCASIFTFeature> featuresA = null;
                List<PCASIFTFeature> featuresB = null;
                PCAKeypointProjector projector = new PCAKeypointProjector(eigenspace, false);

                var taskA = Task.Run(() =>
                {
                    featuresA = new PCASIFTDetector().Detect(model, null).Cast<PCASIFTFeature>().ToList();
                    projector.Project(model, featuresA, 1);
                });
                var taskB = Task.Run(() =>
                {
                    featuresB = new PCASIFTDetector().Detect(data, null).Cast<PCASIFTFeature>().ToList();
                    projector.Project(data, featuresB, 2);
                });

                taskA.Wait();
                taskB.Wait();

                BruteForceMatcher matcher = new BruteForceMatcher();
                matches = matcher.Match(new ImageRef(model), new ImageRef(data), featuresA, featuresB);
            }

            else if (matchAlg == MatchAlg.ASIFT)
            {
                Trace.WriteLine("Matching with ASIFT...");
                matches = ASIFT(model, data, outputFile);
            }

            return matches;


        }

        void FilterAndSave(ImagePairCorrespondence matches, Filter filter, string outputFile)
        {
            ImagePairCorrespondence matchesCopy = null;
            if (filter == Filter.MoisanStival)
            {
                Trace.WriteLine("Filtering with Moisan Stival...");
                MoisanStivalFilter MSfilter = new MoisanStivalFilter();
                matchesCopy = MSfilter.Filter(matches);
            }
            else if (filter == Filter.GTM)
            {
                Trace.WriteLine("Filtering with GTM...");
                GTM gtm = new GTM(5);
                matchesCopy = gtm.Filter(matches);
            }
            else if (filter == Filter.MoisanStival_GTM)
            {
                Trace.WriteLine("Filtering with MoisanStival and GTM...");
                MoisanStivalFilter MSfilter = new MoisanStivalFilter();
                matchesCopy = MSfilter.Filter(matches);

                if (matchesCopy == null)
                {
                    Trace.WriteLine("No matches found. :(");
                    return;
                }

                GTM gtm = new GTM(5);
                matchesCopy = gtm.Filter(matchesCopy);
            }

            if (matchesCopy == null) {
                Trace.WriteLine("No matches found. :(");
                return;
            }

            MatchImage.WriteMatchImage(matchesCopy, outputFile);
            Trace.WriteLine(string.Format("Matched images written to {0}", outputFile));
        }

        public ImagePairCorrespondence ASIFT(Imaging.Image model, Imaging.Image data, string outputFile)
        {
            Image<Gray, byte> imageModel = model.ToEmguGrayscale();
            Image<Gray, byte> imageData = data.ToEmguGrayscale();

            ASIFTDetector asift = new ASIFTDetector();

            List<SIFTFeature> modelfeat = new List<SIFTFeature>();
            List<SIFTFeature> datafeat = new List<SIFTFeature>();

            var taskA = Task.Run(() =>
            {
                modelfeat = asift.Detect(imageModel, null).Cast<SIFTFeature>().ToList();
            });
            var taskB = Task.Run(() =>
            {
                datafeat = asift.Detect(imageData, null).Cast<SIFTFeature>().ToList();
            });

            taskA.Wait();
            taskB.Wait();

            Matrix<float> descr0 = ToDescriptorMatrix(modelfeat);
            Matrix<float> descr1 = ToDescriptorMatrix(datafeat);
            VectorOfKeyPoint kp0 = ToVOKP(modelfeat);
            VectorOfKeyPoint kp1 = ToVOKP(datafeat);

            BruteForceMatcher matcher = new BruteForceMatcher();
            return matcher.Match(new ImageRef(model), new ImageRef(data), modelfeat, datafeat);
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
