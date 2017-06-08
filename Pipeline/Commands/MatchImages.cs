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

        [Value(1, Required = true, HelpText = "Indicate saving location for gathered patches")]
        public string Outfile { get; set; }

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
            int patchsize = 41;
            string file = "C:\\Users\\charchut\\Downloads\\rock5lol.jpg";
            Image<Gray, byte> modelImage = Imaging.Image.Load(imageFile).ToEmguGrayscale();
            Image<Gray, float> newModelImage = modelImage.Convert<Gray, float>();
            Debug.WriteLine(modelImage.GetType());
            modelImage.Save(file);

            
            SIFT siftCPU = new SIFT();
            MKeyPoint[] mKeypoints = mKeypoints = siftCPU.Detect(modelImage);

            List<PCAKeypoint> keypoints = PCA_SIFT.getPatches(newModelImage, mKeypoints, patchsize);
            PCA_SIFT.writePatchesToFile(keypoints, outputFile, patchsize);

            return 0;
        }
    }
}
