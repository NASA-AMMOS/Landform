using JPLOPS.Imaging;

namespace JPLOPS.Pipeline
{
    public class ImageStats : JsonDataProduct
    {
        public int Bands;
        public int Width;
        public int Height;

        public int NumValid;

        //Here luminance is the L in L(AB) and is in the range [0,100]
        public double LuminanceMin;
        public double LuminanceMax;
        public double LuminanceAverage;
        public double LuminanceStandardDeviation;
        public double LuminanceMedian;
        public double LuminanceMedianAbsoluteDeviation;

        //Here hue is the H in H(SV) and is in the range [0,360]
        //only computed for pixels that pass a color filter
        //which typically enforces min limits on saturation and value
        public double HueAverage;
        public double HueMedian;

        public ImageStats() { }

        public ImageStats(Image img, bool useMask = true)
        {
            Bands = img.Bands;
            Width = img.Width;
            Height = img.Height;
            NumValid = img.ComputeStats(out LuminanceMin, out LuminanceMax, out LuminanceAverage,
                                        out LuminanceStandardDeviation, out LuminanceMedian,
                                        out LuminanceMedianAbsoluteDeviation, out HueAverage, out HueMedian, useMask);
        }
    }
}
