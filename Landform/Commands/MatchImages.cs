using CommandLine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Imaging.Emgu;
using OPS.Alignment;
using System.Diagnostics;
using OPS.Imaging;

namespace OPS.Pipeline
{

    [Verb("matchimages", HelpText = "")]
    public class MatchImagesOptions
    {
        [Value(0, Required = false, HelpText = "")]
        public string ImageA { get; set; }

        [Value(1, Required = false, HelpText = "")]
        public string ImageB { get; set; }

        [Option('p', "patches", Required = false, HelpText = "Indicate saving location for gathered patches")]
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

            if (trainingPath != null)
            {
                Train(trainingFile, trainingPath);
                return 0;
            }

            if (outputFile == null)
            {
                outputFile = imageFileA.Substring(0, imageFileA.Length-4) + ".jpg";
            }
            Image model = Image.Load(imageFileA);
            Image data =  Image.Load(imageFileB);
            DetectAndMatch(model, data, gpcafile, outputFile);

            return 1;
        }

        void DetectAndMatch(Image model, Image data, string gpcafile, string outputFile)
        {
            Trace.WriteLine("Matching images...");
            List<PCA_SIFTFeature> featuresA = new PCA_SIFTDetector().Detect(model, null).Cast<PCA_SIFTFeature>().ToList();
            List<PCA_SIFTFeature> featuresB = new PCA_SIFTDetector().Detect(data, null).Cast<PCA_SIFTFeature>().ToList();
            PCA_KeypointDetector detector = new PCA_KeypointDetector(gpcafile);

            detector.ProjectKeypoints(model, featuresA);
            detector.ProjectKeypoints(data, featuresB);

            if (outputFile == null) { outputFile = trainingFile + ".png"; }
            PCA_Match.Match(model.ToEmguGrayscale(), data.ToEmguGrayscale(), featuresA, featuresB, outputFile);
            Trace.WriteLine("Images matched");
        }

        void Train(string trainingFile, string trainingPath)
        {
            if (trainingFile == null) { trainingFile = trainingPath; }
            Trace.WriteLine("Training...");
            PCA_Train train = new PCA_Train(trainingFile);
            train.Train(trainingPath);
            Trace.WriteLine("Trained.");
        }
    }
}
