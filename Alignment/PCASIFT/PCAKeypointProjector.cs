using System;
using System.Collections.Generic;
using System.Linq;
using Emgu.CV;
using Emgu.CV.Structure;
using Emgu.CV.CvEnum;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using OPS.Imaging.Emgu;

namespace OPS.Alignment.PCASIFT
{
    /// <summary>
    /// PCA Keypoint Projector class.
    /// </summary>
    public class KeypointProjector
    {
        const double PI = 3.14159256358979323846;
        const int PATCHMAG = 20;
        const int PATCHSIZE = 41;
        const double INIT_SIGMA = 0.5;
        static float SIGMA = 1.6F;
        const int SCALES_PER_OCTAVE = 3;
        const int MAX_OCTAVES = 14;
        static int DOUBLE_BASE_IMAGE_SIZE = 1;
        const int GPLEN = (PATCHSIZE - 2) * (PATCHSIZE - 2) * 2;
        const int PCALEN = 36;
        const int EPCALEN = 36;
        float[] avgs = new float[GPLEN];
        float[,] eigs = new float[EPCALEN, GPLEN];

        /// <summary>
        /// Initializes a new instance of the <see cref="T:OPS.Alignment.PCA_KeypointDetector"/> class.
        /// </summary>
        /// <param name="file">File containing mean and eigenspace computed from training set.</param>
        public KeypointProjector(string file, bool textFile = false)
        {
            if (File.Exists(file))
            {
                if (!textFile)
                {
                    using (BinaryReader reader = new BinaryReader(new FileStream(file, FileMode.Open)))
                    {
                        reader.BaseStream.Position = 0;
                        Debug.WriteLine("Reading averages.");
                        for (int i = 0; i < GPLEN; i++)
                        {
                            avgs[i] = reader.ReadSingle();
                        }

                        Debug.WriteLine("Reading pca vector {0}x{1}", GPLEN, EPCALEN);
                        for (int i = 0; i < GPLEN; i++)
                        {
                            for (int j = 0; j < EPCALEN; j++)
                            {

                                eigs[j, i] = reader.ReadSingle();
                            }
                        }
                    }
                }
                else
                {
                    using (TextReader reader = File.OpenText(file))
                    {
                        string[] numbers0 = reader.ReadToEnd().Split(new char[] {'\n', ' '});
                        List<string> numbers = new List<string>();

                        foreach (string num in numbers0)
                        {
                            if (num != "") numbers.Add(num);
                        }


                        Debug.WriteLine("Reading averages");
                        int count = 0;

                        for (int i = 0; i < GPLEN; i++)
                        {
                            avgs[i] = float.Parse(numbers[count++]);
                        }

                        Debug.WriteLine("Reading pca vector {0}x{1}", GPLEN, PCALEN);
                        for (int i = 0; i < GPLEN; i++)
                        {
                            for (int j = 0; j < PCALEN; j++)
                            {

                                eigs[j, i] = float.Parse(numbers[count++]);
                            }
                        }
                    }
                }
            }
        }

		/// <summary>
		/// Create PCA descriptor for instance of <see cref="T:OPS.Alignment.PCA_Keypoint"/>.
		/// </summary>
		/// <param name="keypoint">Keypoint.</param>
		/// <param name="blur">Blur.</param>
		void MakeKeypointPCA(PCASIFTFeature keypoint, Image<Gray, float> blur)
        {
            float[] vec = KeypointPatchVector(keypoint, blur);
            Util.NormalizeVector(vec);

            for (int i = 0; i < GPLEN; i++)
            {
                vec[i] -= avgs[i];
            }

            float[] result = new float[EPCALEN];

            for (int desci = 0; desci < EPCALEN; desci++)
            {
                float total = 0;

                for (int x = 0; x < GPLEN; x++)
                {
                    total += eigs[desci, x] * vec[x];
                }

                result[desci] = total;
            }
            keypoint.Descriptor = new PCASIFTDescriptor(result); 
        }

        /// <summary>
        /// Calculates a gradient vector representing an keypoint's associated patch.
        /// </summary>
        /// <param name="keypoint">Keypoint.</param>
        /// <param name="blur">Blur.</param>
        float[] KeypointPatchVector(PCASIFTFeature keypoint, Image<Gray, float> blur)
        {
            float[] vec = new float[GPLEN];

            int patchsize, iradius;
            float sine, cosine, sizeratio;

            float scale = SIGMA * (float)Math.Pow(2.0, keypoint.FScale / SCALES_PER_OCTAVE);

            // Sampling window size
            patchsize = (int)(PATCHMAG * scale);

            // Make odd
            patchsize = (patchsize / 2) * 2 + 1;

            // Technically a bug fix but should do the trick for now
            if (patchsize < PATCHSIZE)
            {
                patchsize = PATCHSIZE;
            }

            sizeratio = patchsize / (float)PATCHSIZE;
            Image<Gray, float> patch = new Image<Gray, float>(patchsize, patchsize);
            float[,,] data = patch.Data;

            sine = (float)Math.Sin(keypoint.Angle);
            cosine = (float)Math.Cos(keypoint.Angle);

            iradius = patchsize / 2;

            float[,,] blurData = blur.Data;
            int height = blur.Height;
            int width = blur.Width;

            float cpos, rpos;
            for (int y = -iradius; y <= iradius; y++)
            {
                for (int x = -iradius; x <= iradius; x++)
                {
                    cpos = (cosine * x  + sine * y) + keypoint.SX;
                    rpos = (-sine * x + cosine * y) + keypoint.SY;
                    data[x + iradius, y + iradius, 0] = Util.GetPixelBilinearInterpolation(blurData, cpos, rpos, height, width);
                }
            }

            int count = 0;
            float x1, x2, y1, y2, gx, gy;
            for (int y = 1; y < PATCHSIZE - 1; y++)
            {
                for (int x = 1; x < PATCHSIZE - 1; x++)
                {
                    x1 = Util.GetPixelBilinearInterpolation(data, y * sizeratio, (x + 1) * sizeratio, height, width)/255;
                    x2 = Util.GetPixelBilinearInterpolation(data, y * sizeratio, (x - 1) * sizeratio, height, width)/255;
                    y1 = Util.GetPixelBilinearInterpolation(data, (y + 1) * sizeratio, x * sizeratio, height, width)/255;
                    y2 = Util.GetPixelBilinearInterpolation(data, (y - 1) * sizeratio, x * sizeratio, height, width)/255;

                    gx = x1 - x2;
                    gy = y1 - y2;

                    vec[count++] = gx;
                    vec[count++] = gy;    
                }
            }

            return vec;
        }

        /// <summary>
        /// Computes local descriptors for a set of keypoints given their corresponding Gaussian octaves.
        /// </summary>
        /// <param name="keypoints">list of <see cref="T:OPS.Alignment.PCA_Keypoint"/> instances</param>
        /// <param name="octaves">List of Guassian scales calculated for each octave.</param>
        void ComputeLocalDescriptors(List<PCASIFTFeature> keypoints, List<List<Image<Gray, float>>> octaves)
        {
            Parallel.For(0, keypoints.Count(), i =>
            {
                PCASIFTFeature key = keypoints[i];
                MakeKeypointPCA(keypoints[i], octaves[key.Octave][key.Scale]);
            });
        }

        /// <summary>
        /// Projects keypoints onto PCA-dimension and determines local descriptors.
        /// </summary>
        /// <param name="image">Input image.</param>
        /// <param name="keypoints">List of keypoints.</param>
        public void Project(Imaging.Image image, List<PCASIFTFeature> keypoints, int number = 0)
        {
            Image<Gray, byte> imByte = image.ToEmguGrayscale();
            Image<Gray, float> im = imByte.Convert<Gray, float>();

            im = Util.ScaleInitImage(im);
            List<List<Image<Gray, float>>> GOctaves = Util.BuildGaussianOctaves(im);
            Util.UpdateKeypoints(keypoints);
            ComputeLocalDescriptors(keypoints, GOctaves);
        }
    }    
}
