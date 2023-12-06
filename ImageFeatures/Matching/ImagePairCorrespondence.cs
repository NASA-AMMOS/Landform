using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using JPLOPS.Geometry;

namespace JPLOPS.ImageFeatures
{
    /// <summary>
    /// Represents a computed correspondence between visual features in a pair of images.
    /// </summary>
    public class ImagePairCorrespondence
    {
        //these aren't readonly so that they can be deserialized from json
        public string ModelImageUrl, DataImageUrl;
        public KeyValuePair<int,int>[] DataToModel;
        public double[] DescriptorDistance;
        public EpipolarMatrix FundamentalMatrix;
        public Matrix? BestTransformEstimate;

        public int Count
        {
            get { return DataToModel.Length; }
        }

        public static ImagePairCorrespondence Empty = new ImagePairCorrespondence(null, null, null);

        public ImagePairCorrespondence(string modelUrl, string dataUrl, IEnumerable<FeatureMatch> matches,
                                        EpipolarMatrix fundamentalMat = null, Matrix? estimate = null)
        {
            this.ModelImageUrl = modelUrl;
            this.DataImageUrl = dataUrl;
            this.FundamentalMatrix = fundamentalMat;
            this.BestTransformEstimate = estimate;
            var dataToModel = new List<KeyValuePair<int, int>>();
            var descriptorDistance = new List<double>();
            foreach (var match in (matches ?? new FeatureMatch[] {}).OrderByDescending(m => m.DescriptorDistance))
            {
                dataToModel.Add(new KeyValuePair<int, int>(match.DataIndex, match.ModelIndex));
                descriptorDistance.Add(match.DescriptorDistance);
            }
            this.DataToModel = dataToModel.ToArray();
            this.DescriptorDistance = descriptorDistance.ToArray();
        }

        public ImagePairCorrespondence() { }
    }
}
