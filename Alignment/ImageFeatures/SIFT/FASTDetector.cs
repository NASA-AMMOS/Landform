using System;
using System.Collections.Generic;
using System.Linq;
using Emgu.CV;
using Emgu.CV.Structure;
using Emgu.CV.Features2D;
using Emgu.CV.XFeatures2D;
using Emgu.CV.Util;
using OPS.Imaging;
using OPS.Imaging.Emgu;

namespace OPS.Alignment
{
    public class FASTDetector : FeatureDetectorBase
    {
        public int Threshold = 10;
        public bool NonMaxSuppression = true;
        public FastDetector.DetectorType DetectorType = FastDetector.DetectorType.Type9_16;
        public int DescriptorSize = 64; //16, 32, or 64

        public override Feature2D MakeDetector()
        {
            return new FastDetector(Threshold, NonMaxSuppression, DetectorType);
        }

        public override Feature2D MakeExtractor()
        {
            //return new BriefDescriptorExtractor(DescriptorSize);

            //for some reason BriefDescriptorExtractor is returning all zeros
            //so for now just use SIFT descriptors
            return new SIFT();
        }
    }
}
