using JPLOPS.MathExtensions;

namespace JPLOPS.Imaging
{

    /// <summary>
    /// Computes per band statistics for an image such as average value and standard deviation
    /// </summary>
    public class ImageStatistics
    {
        RunningAverage[] bandAverages;

        /// <summary>
        /// Generate statistics for the provided image
        /// </summary>
        /// <param name="image"></param>
        public ImageStatistics(Image image)
        {
            bandAverages = new RunningAverage[image.Bands];
            for(int i = 0; i < bandAverages.Length; i++)
            {
                bandAverages[i] = new RunningAverage();
            }
            foreach(var coord in image.Coordinates(false))
            {
                bandAverages[coord.Band].Push(image[coord.Band, coord.Row, coord.Col]);
            }
        }

        /// <summary>
        /// Get stats for the specified band
        /// </summary>
        /// <param name="band"></param>
        /// <returns></returns>
        public RunningAverage Average(int band)
        {
            return bandAverages[band];
        }
    }
}
