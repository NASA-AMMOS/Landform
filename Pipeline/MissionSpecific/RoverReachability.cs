using JPLOPS.Imaging;

namespace JPLOPS.Pipeline
{
    public class RoverReachability
    {
        /// <summary>
        /// Given an image and a corresponding reachability product, create a new image where alpha is determined by 
        /// reachability.  Alpha will be 1 in areas that are reachable, other areas it will be set to the supplied value.
        /// A pixel is deemed reachable if it is reachable in any channel of the input product.
        /// </summary>
        /// <param name="img"></param>
        /// <param name="reachability"></param>
        /// <param name="unreachableAlpha"></param>
        /// <returns></returns>
        public static Image GenerateAlphaFromArmImage(Image img, Image reachability, float unreachableAlpha = 0.75f)
        {
            Image ret = new Image(img);
            float[] alphaChannel = ret.GetBandData(ret.AddBand());
            float[][] reachabilityChannel = new float[reachability.Bands][];
            for (int b = 0; b < reachability.Bands; b++)
            {
                reachabilityChannel[b] = reachability.GetBandData(b);
            }
            for (int i = 0; i < alphaChannel.Length; i++)
            {
                alphaChannel[i] = unreachableAlpha;
                for (int b = 0; b < reachability.Bands; b++)
                {
                    if (reachabilityChannel[b][i] != 0)
                    {
                        alphaChannel[i] = 1;
                        break;
                    }
                }
            }
            return ret;
        }
    }
}
