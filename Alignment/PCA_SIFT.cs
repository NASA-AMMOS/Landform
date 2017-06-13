using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Emgu.CV;
using Emgu.CV.Structure;
using Emgu.CV.CvEnum;
using Microsoft.Xna.Framework;
using Emgu.CV.XFeatures2D;
using Emgu.CV.Features2D;
using Emgu.CV.Util;
using System.Drawing;
using OPS.Imaging.Emgu;
using System.Diagnostics;
using System.IO;

namespace OPS.Alignment
{
    // As described in PCA SIFT : A More Distinctive Representation for Local Image Descriptors
    // http://www.cs.cmu.edu/~yke/pcasift/

    public class PCA_SIFT
    {
        const double PI = 3.14159256358979323846;
        const int PATCHMAG = 20;
        const int PATCHSIZE = 41;
        const double INIT_SIGMA = 0.5;
        static float SIGMA = 1.6F;
        const int SCALES_PER_OCTAVE = 5;
        const int MAX_OCTAVES = 14;
        static int DOUBLE_BASE_IMAGE_SIZE = 1;
        const int GPLEN = (PATCHSIZE - 2) * (PATCHSIZE - 2) * 2;

        static IEnumerable<ImageFeature> DetectSIFT(Image<Gray, byte> image, Image<Gray, byte> mask = null, int numFeatures = 1000, int numOctaves = 4)
        {
            using (SIFT sift = new SIFT(numFeatures, numOctaves))
            {
                MKeyPoint[] keypoints = sift.Detect(image, mask);
                if (keypoints.Length < 3) yield break;

                using (Matrix<float> descriptors = new Matrix<float>(keypoints.Length, 128))
                {
                    sift.Compute(image, new VectorOfKeyPoint(keypoints), descriptors);

                    int i;
                    for (i = 0; i < keypoints.Length; i++)
                    {
                        float[] desc = new float[128];
                        for (int j = 0; j < 128; j++)
                        {
                            desc[j] = descriptors[i, j];
                        }
                        yield return new ImageFeature(
                            new Vector2(keypoints[i].Point.X, keypoints[i].Point.Y),
                            keypoints[i].Size,
                            keypoints[i].Angle,
                            desc
                           );
                    }
                }
            }
        }

        public static Image<Gray, byte> PutFeaturesOnImage(string file)
        {
            Image<Gray, byte> modelImage = new Image<Gray, byte>(file);
            SIFT siftCPU = new SIFT(20);

            MKeyPoint[] keypoints = siftCPU.Detect(modelImage);
            if (keypoints.Length < 3) return modelImage;

            VectorOfKeyPoint modelKeyPoints = new VectorOfKeyPoint(keypoints);

            Features2DToolbox.KeypointDrawType type = Features2DToolbox.KeypointDrawType.Default;
            Image<Bgr, byte> image = new Image<Bgr, byte>(file);
            Features2DToolbox.DrawKeypoints(modelImage, modelKeyPoints, image, new Bgr(Color.Red), type);
            image.Save(file + " lol.png");

            return modelImage;
        }


    }
}
