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

        public override void AddDescriptors(Image<Gray, byte> image, IEnumerable<ImageFeature> features)
        {
            var keypoints = features.Cast<SIFTFeature>().CastToMKeyPoint().ToArray();
            if (keypoints.Length == 0)
            {
                return;
            }
            var detector = new BriefDescriptorExtractor(DescriptorSize);
            var descriptorMatrix = new Matrix<float>(keypoints.Length, DescriptorSize);
            detector.Compute(image, new VectorOfKeyPoint(keypoints), descriptorMatrix);
            int i = 0;
            foreach (var feature in features)
            {
                byte[] data = new byte[DescriptorSize];
                for (int j = 0; j < DescriptorSize; j++)
                {
                    data[j] = (byte)descriptorMatrix.Data[i, j];
                }
                feature.Descriptor = new BRIEFDescriptor(data);
                i++;
            }
        }
    }
}
