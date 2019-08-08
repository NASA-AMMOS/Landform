using OPS.Imaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Pipeline
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
            Image r = new Image(img);
            float[][] origData = r.Data;
            r.Data = new float[img.Bands + 1][];
            r.Data[img.Bands] = new float[img.Width * img.Height];
            r.Bands += 1;

            for (int b = 0; b < img.Bands; b++)
            {
                r.Data[b] = origData[b];
            }
            for (int i = 0; i < r.Data[0].Length; i++)
            {
                r.Data[r.Bands - 1][i] = unreachableAlpha;
                for (int b = 0; b < reachability.Bands; b++)
                {
                    if (reachability.Data[b][i] != 0)
                    {
                        r.Data[r.Bands - 1][i] = 1;
                        break;
                    }
                }
            }
            return r;
        }
    }
}
