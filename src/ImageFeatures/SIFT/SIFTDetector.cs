using Emgu.CV.Features2D;
using Emgu.CV.XFeatures2D;

namespace JPLOPS.ImageFeatures
{
    /// <summary>
    /// http://www.cs.ubc.ca/~lowe/papers/ijcv04.pdf
    /// </summary>
    public class SIFTDetector : FeatureDetectorBase
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

        public override Feature2D MakeDetector()
        {
            return new SIFT(MaxNumFeatures, OctaveLayers, ContrastThreshold, EdgeThreshold, Sigma);
        }
    }
}
