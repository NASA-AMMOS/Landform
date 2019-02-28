namespace OPS.Alignment
{
    /// <summary>
    /// Interface for feature matching strategies.
    /// </summary>
    public interface IFeatureMatcher
    {
        /// <summary>
        /// Match features between a pair of images and return the
        /// set of corresponding points.
        /// </summary>
        ImagePairCorrespondence Match(AlignmentScene scene, string modelUrl, string dataUrl);

        ImagePairCorrespondence Match(ImageFeature[] modelFeatures, ImageFeature[] dataFeatures,
                                      string modelUrl, string dataUrl);
    }
}
