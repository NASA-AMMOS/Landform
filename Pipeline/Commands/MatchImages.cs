using CommandLine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Emgu.CV;
using Emgu.CV.Structure;
using Emgu.CV.CvEnum;
using Microsoft.Xna.Framework;
using Emgu.CV.XFeatures2D;
using Emgu.CV.Util;
using Emgu.CV.Features2D;
using System.Drawing;
using Emgu.Util;
using OPS.Imaging.Emgu;
using OPS.Alignment;
using System.Diagnostics;

namespace OPS.Pipeline
{

    [Verb("matchimages", HelpText = "")]
    public class MatchImagesOptions
    {
        [Value(0, Required = true, HelpText = "")]
        public string ImageA { get; set; }

        //[Value(1, Required = true, HelpText = "")]
        //public string ImageB { get; set; }

        [Value(1, Required = false, HelpText = "Indicate saving location for gathered patches")]
        public string Outfile { get; set; }

        [Option('t', "train", Required = false, HelpText = "Indicate directory of training images")]
        public string TrainingPath { get; set; }

        [Option('o', "trainoutput", Required = false, HelpText = "Indicate saving location for computed eigenspace")]
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
            string imageFile = options.ImageA;
            string outputFile = options.Outfile;
            string trainingPath = options.TrainingPath;
            string trainingFile = options.TrainingFile;

            int patchsize = 41;

            Image<Gray, byte> modelImage = Imaging.Image.Load(imageFile).ToEmguGrayscale();
            Image<Gray, float> grayModelImage = modelImage.Convert<Gray, float>();

            string gpcafile = trainingFile; // "C:\\Users\\charchut\\Downloads\\gpcavects.txt";
            string gradfile = "C:\\Users\\charchut\\Downloads\\grads.txt";

            // 0. Pre-compute eigenspace on images
            //PCA_Train train = new PCA_Train(gradfile);
            //SIFT siftCPUt = new SIFT();
            //MKeyPoint[] mKeypointst = siftCPUt.Detect(modelImage);
            //List<PCA_Keypoint> keypointst = PCA_KeypointDetector.getPatches(grayModelImage, mKeypointst, patchsize);
            //PCA_Train train = new PCA_Train(PCA_KeypointDetector.getGradients(keypointst));

            if (trainingPath != null && trainingFile != null)
            {
                PCA_Train train = new PCA_Train(trainingFile);
                train.Train(trainingPath);
            }
           
            //train.writeEigsToFile(gpcafile);

            // 1. Calculate keypoints of an image using SIFT detection
            SIFT siftCPU = new SIFT();
            MKeyPoint[] mKeypoints = siftCPU.Detect(modelImage);
            List<PCA_Keypoint> keypoints = PCA_KeypointDetector.getPatches(grayModelImage, mKeypoints, patchsize);
            PCA_KeypointDetector.writePatchesToFile(keypoints, "C:\\Users\\charchut\\Downloads\\patches.txt", patchsize);
            PCA_KeypointDetector.writeGradientsToFile(keypoints, gradfile);

            // 2. Recalculate keypoints, given the eigenspace, an image, and its keypoints
            //// e.g. ./recalckeys gpcavects.txt image1.pgm image2.pgm image1.lkeys image1.pkeys    
            PCA_KeypointDetector detector = new PCA_KeypointDetector(gpcafile);
            detector.recalculateKeys(grayModelImage, keypoints);

            // 3. Return list of keypoints with updated descriptors?

            return 0;
        }
    }
}
