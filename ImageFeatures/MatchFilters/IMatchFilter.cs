namespace JPLOPS.ImageFeatures
{
    /// <summary>
    /// Interface for image correspondence filters.
    /// </summary>
    public interface IMatchFilter
    {
        ImagePairCorrespondence Filter(ImageFeature[] modelFeatures, ImageFeature[] dataFeatures,
                                       ImagePairCorrespondence matches);
    }

}
