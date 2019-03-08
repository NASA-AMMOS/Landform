using System;
using System.Collections.Generic;
using System.Linq;
using Emgu.CV;
using Emgu.CV.Structure;
using Emgu.CV.XFeatures2D;
using Emgu.CV.Util;
using OPS.Imaging;
using OPS.Imaging.Emgu;

namespace OPS.Alignment
{
    /// <summary>
    /// http://www.cs.ubc.ca/~lowe/papers/ijcv04.pdf
    /// </summary>
    public class SIFTDetector : IFeatureDetector
    {
        //parameter docs come from a combination of
        //http://www.emgu.com/wiki/files/3.0.0/document/html/ca75809a-082d-dcda-6fcf-aaf738e179f0.htm
        //https://github.com/robwhess/opensift/blob/master/src/sift.c
        
        public int MaxNumFeatures = 0; //The desired number of features. Use 0 for un-restricted number of features

        public int OctaveLayers = 3; //The number of octave layers. Use 3 for default

        //a threshold on the value of the scale space function
        //\f$\left|D(\hat{x})\right|\f$, where \f$\hat{x}\f$ is a vector specifying
        //feature location and scale, used to reject unstable features;  assumes
        //pixel values in the range [0, 1]
        //Use 0.04 as default
        public float ContrastThreshold = 0.04f;

        //threshold on a feature's ratio of principle curvatures
        //used to reject features that are too edge-like
        //Use 10.0 as default
        public float EdgeThreshold = 10.0f;

        //the amount of Gaussian smoothing applied to each image level
        //before building the scale space representation for an octave
        //Use 1.6 as default
        public float Sigma = 1.6f;

        public virtual IEnumerable<ImageFeature> Detect(Image image, Image mask = null)
        {
            return Detect(image.ToEmguGrayscale(), (mask != null) ? mask.ToEmguGrayscale() : null);
        }

        public IEnumerable<ImageFeature> Detect(Image<Gray, byte> image, Image<Gray, byte> mask = null)
        {
            foreach (var keypoint in DetectKeypoints(image, mask))
            {
                yield return new SIFTFeature(keypoint);
            }
        }

        public MKeyPoint[] DetectKeypoints(Image<Gray, byte> image, Image<Gray, byte> mask = null)
        {
            var sift = new SIFT(MaxNumFeatures, OctaveLayers, ContrastThreshold, EdgeThreshold, Sigma);
            return sift.Detect(image, mask);
        }

        public virtual void AddDescriptors(Image image, IEnumerable<ImageFeature> features)
        {
            AddDescriptors(image.ToEmguGrayscale(), features);
        }

        public virtual void AddDescriptors(Image<Gray, byte> image, IEnumerable<ImageFeature> features)
        {
            var sift = new SIFT(MaxNumFeatures, OctaveLayers, ContrastThreshold, EdgeThreshold, Sigma);
            var keypoints = features.Cast<SIFTFeature>().CastToMKeyPoint().ToArray();
            var descriptorMatrix = new Matrix<float>(keypoints.Length, 128);
            sift.Compute(image, new VectorOfKeyPoint(keypoints), descriptorMatrix);
            int i = 0;
            foreach (var feature in features)
            {
                byte[] data = new byte[128];
                for (int j = 0; j < 128; j++)
                {
                    data[j] = (byte)descriptorMatrix.Data[i, j];
                }
                feature.Descriptor = new SIFTDescriptor(data);
                i++;
            }
        }
    }
}
