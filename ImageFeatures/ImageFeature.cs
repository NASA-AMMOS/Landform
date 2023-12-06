using Microsoft.Xna.Framework;
using System.Linq;
using Emgu.CV;
using Emgu.CV.Util;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Features2D;
using JPLOPS.Imaging;
using JPLOPS.Imaging.Emgu;

namespace JPLOPS.ImageFeatures
{
    public class ImageFeature
    {
        public enum DetectorType
        {
            SIFT,
            ASIFT,
            PCASIFT,
            FAST
        }

        public double Range = 0; //positive iff feature has valid associated range
        public Vector2 Location;
        public FeatureDescriptor Descriptor;

        //needed for JSON deserialization
        public ImageFeature() { }

        public ImageFeature(Vector2 location, FeatureDescriptor descriptor)
        {
            this.Location = location;
            this.Descriptor = descriptor;
        }

        public static Image DrawFeatures(Image img, Image mask, ImageFeature[] features, string imageName = null,
                                         bool stretch = true)
        {
            return DrawFeaturesEmgu(img, mask, features, imageName, stretch).ToOPSImage();
        }

        public static Image<Bgr, byte> DrawFeaturesEmgu(Image img, Image mask, ImageFeature[] features,
                                                        string imageName = null, bool stretch = true)
        {
            var ret = stretch ?  (new Image(img)).ApplyStdDevStretch().ToEmgu<Bgr>() : img.ToEmgu<Bgr>();

            //alpha blend mask into green channel
            if (mask != null)
            {
                float alpha = 0.1f;
                for (int row = 0; row < img.Height; row++)
                {
                    for (int col = 0; col < img.Width; col++)
                    {
                        float green = ret.Data[row, col, 1] / 255.0f;
                        green = (1.0f - alpha) * green + alpha * mask[0, row, col];
                        ret.Data[row, col, 1] = (byte)(green * 255);
                    }
                }
            }

            var siftFeat = features.Cast<SIFTFeature>().ToArray();
            var noRange = new VectorOfKeyPoint(siftFeat.Where(f => !(f.Range > 0)).CastToMKeyPoint().ToArray());
            Features2DToolbox.DrawKeypoints(ret, noRange, ret, new Bgr(255, 0, 0), //actually RGB
                                            Features2DToolbox.KeypointDrawType.DrawRichKeypoints);
            var withRange = new VectorOfKeyPoint(siftFeat.Where(f => f.Range > 0).CastToMKeyPoint().ToArray());
            Features2DToolbox.DrawKeypoints(ret, withRange, ret, new Bgr(0, 255, 0), //actually RGB
                                            Features2DToolbox.KeypointDrawType.DrawRichKeypoints);
            if (imageName != null)
            {
                ret.Draw(imageName, new System.Drawing.Point(5, 30),
                         FontFace.HersheySimplex, 1, new Bgr(255, 0, 255), 2);
            }
            return ret;
        }
    }
}
