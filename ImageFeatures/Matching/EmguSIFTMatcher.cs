using System.Collections.Generic;
using System.Linq;
using Emgu.CV;
using Emgu.CV.Features2D;
using Emgu.CV.Util;

namespace JPLOPS.ImageFeatures
{
    public class EmguSIFTMatcher : IFeatureMatcher
    {
        //maxumum ratio between distance of nearest data feature descriptor to model feature descriptor
        //vs 2nd nearest data feature descriptor to the same model feature descriptor
        //set to 1 to disable filtering by this ratio
        public double MaxDistanceRatio = 0.9;

        //whether to use EmguCV VoteForSizeAndOrientation
        public bool VoteForSizeAndOrientation = true;

        //minimum distance ratio to consider two feature matches unique
        //set to 0 to disable uniqueness filtering
        public double MinUniquenessRatio = 0.8;

        public ImagePairCorrespondence Match(ImageFeature[] modelFeatures, ImageFeature[] dataFeatures,
                                             string modelUrl, string dataUrl)
        {
            return new ImagePairCorrespondence(modelUrl, dataUrl, Match(modelFeatures, dataFeatures));
        }

        public IEnumerable<FeatureMatch> Match(ImageFeature[] modelFeatures, ImageFeature[] dataFeatures)
        {
            if (modelFeatures.Length < 1 || dataFeatures.Length < 1) yield break;

            Matrix<float> modelDescriptors = FeatureDescriptor.ToDescriptorMatrix(modelFeatures);
            Matrix<float> dataDescriptors = FeatureDescriptor.ToDescriptorMatrix(dataFeatures);
            VectorOfKeyPoint modelKeypoints =
                new VectorOfKeyPoint(modelFeatures.Cast<SIFTFeature>().CastToMKeyPoint().ToArray());
            VectorOfKeyPoint dataKeypoints =
                new VectorOfKeyPoint(dataFeatures.Cast<SIFTFeature>().CastToMKeyPoint().ToArray());

            //find 2 closest model features for each data feature
            Matrix<int> indices = new Matrix<int>(dataDescriptors.Rows, 2);
            VectorOfVectorOfDMatch matches = new VectorOfVectorOfDMatch();
            using (BFMatcher bfm = new BFMatcher(DistanceType.L2))
            {
                bfm.Add(modelDescriptors);
                bfm.KnnMatch(dataDescriptors, matches, 2, null);
            }

            Matrix<byte> mask = new Matrix<byte>(matches.Size, 1);
            mask.SetValue(1);

            // OpenCV standard correspondence checks

            if (MaxDistanceRatio < 1) 
            {
                int keepers = matches.Size;
                for (int idx = 0; idx < matches.Size; idx++)
                {
                    //keep match iff bestDist/2ndBestDist <= MaxDistanceRatio
                    if (matches[idx][0].Distance > matches[idx][1].Distance * MaxDistanceRatio)
                    {
                        mask[idx, 0] = 0;
                        keepers--;
                    }
                }
                if (keepers == 0)
                {
                    yield break;
                }
            }

            if (VoteForSizeAndOrientation)
            {
                int keepers = Features2DToolbox.VoteForSizeAndOrientation(modelKeypoints, dataKeypoints, matches,
                                                                          mask.Mat, 1.5, 20);
                if (keepers == 0)
                {
                    yield break;
                }
            }

            if (MinUniquenessRatio > 0)
            {
                //unlike VoteForSizeAndOrientation this API does not return the number of nonzeros in the resulting mask
                Features2DToolbox.VoteForUniqueness(matches, MinUniquenessRatio, mask.Mat);
                if (CvInvoke.CountNonZero(mask) == 0)
                {
                    yield break;
                }
            }

            for (int idx = 0; idx < matches.Size; idx++)
            {
                if (mask[idx, 0] != 0)
                {
                    var match = matches[idx][0];
                    yield return new FeatureMatch()
                    {
                        DataIndex = match.QueryIdx,
                        ModelIndex = match.TrainIdx,
                        DescriptorDistance = match.Distance
                    };
                }
            }
        }
    }
}
