using System.Collections.Generic;
using Emgu.CV.XFeatures2D;
using Emgu.CV;
using OPS.Imaging;
using OPS.Imaging.Emgu;
using Emgu.CV.Structure;
using Microsoft.Xna.Framework;
using System.IO;
using System;

namespace OPS.Alignment
{
    public class PCA_SIFTDetector : IFeatureDetector
    {
        SIFT sift;
        // For details, see http://www.cs.ubc.ca/~lowe/papers/ijcv04.pdf
        public PCA_SIFTDetector(int numFeatures = 0, int octaveLayers = 3, float contrastThreshold = 0.04f, float edgeThreshold = 10f, float sigma = 1.6f)
        {
            sift = new SIFT(numFeatures, octaveLayers, contrastThreshold, edgeThreshold, sigma);
        }

        public IEnumerable<ImageFeature> Detect(Image image, Image mask = null)
        {
            var emguImg = image.ToEmguGrayscale();
            //var emguImg = emguImgByte.Convert<Gray, float>();
            Image<Gray, byte> emguMask = (mask != null) ? (mask.ToEmguGrayscale()) : null;

            foreach (var kp in sift.Detect(emguImg, emguMask))
            {
                yield return new PCA_SIFTFeature(
                    new Vector2(kp.Point.X, kp.Point.Y),
                    kp.Size,
                    kp.Angle,
                    kp.Octave,
                    kp.Response);
            }
        }

        public void Write(Image image, string filename, Image mask = null)
        {
            var emguImg = image.ToEmguGrayscale();
            //var emguImg = emguImgByte.Convert<Gray, float>();
            Image<Gray, byte> emguMask = (mask != null) ? (mask.ToEmguGrayscale()) : null;

            using (StreamWriter writer = new StreamWriter(new FileStream(filename, FileMode.Create)))
            {
                MKeyPoint[] kps = sift.Detect(emguImg, emguMask);
                writer.WriteLine("{0} {1}", kps.Length, 128);
                foreach (var kp in kps)
                {
                    writer.WriteLine("{0} {1} {2} {3}", string.Format("{0:0.00}", kp.Point.Y), string.Format("{0:0.00}", kp.Point.X),
                                                        string.Format("{0:0.00}", kp.Size), string.Format("{0:0.000}", kp.Angle / 180 * Math.PI));

                    for (int i = 0; i < 6; i++)
                    {
                        for (int j = 0; j < 20; j++)
                        {
                            writer.Write(" 0");
                        }
                        writer.WriteLine();
                    }
                    for (int k = 0; k < 8; k++)
                    {
                        writer.Write(" 0");
                    }
                    writer.WriteLine(); 
                }
            }
        }
    }
}
