using System;

namespace JPLOPS.Imaging
{
    public class ImageHistogram
    {
        public float BinWidth { get; private set; }

        public int this[int band, int bin]
        {
            get
            {
                return bins[band, bin];
            }
        }

        private int[,] bins;

        //assumes all bands are already normalized to [0.0,1.0]
        public ImageHistogram(Image image, int numBins = 4096)
        {
            BinWidth = 1.0f / numBins;
            bins = new int[image.Bands, numBins];
            foreach(var coord in image.Coordinates(includeInvalidValues: false))
            {
                float val = image[coord.Band, coord.Row, coord.Col];
                int bin = (val <= 0) ? 0 : (val >= 1.0f) ? (numBins - 1) : (int)(val * numBins);
                bins[coord.Band, Math.Max(0, Math.Min(numBins - 1, bin))]++;
            }
        }
    }
}
