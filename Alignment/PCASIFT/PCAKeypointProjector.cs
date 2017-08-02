using System;
using System.Collections.Generic;
using System.Linq;
using Emgu.CV;
using Emgu.CV.Structure;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using OPS.Imaging.Emgu;
using log4net;

namespace OPS.Alignment
{
    /// <summary>
    /// PCA Keypoint Projector class.
    /// </summary>
    public class PCAKeypointProjector
    {
        float[] avgs = new float[PCAConstants.GPLEN];
        float[,] eigs = new float[PCAConstants.EPCALEN, PCAConstants.GPLEN];

        private static readonly ILog logger = LogManager.GetLogger(typeof(PCAKeypointProjector));

        /// <summary>
        /// Initializes a new instance of the <see cref="T:OPS.Alignment.PCA_KeypointDetector"/> class.
        /// </summary>
        /// <param name="file">File containing mean and eigenspace computed from training set.</param>
        public PCAKeypointProjector(string file, bool textFile = false)
        {
            if (File.Exists(file))
            {
                if (!textFile)
                {
                    using (BinaryReader reader = new BinaryReader(new FileStream(file, FileMode.Open)))
                    {
                        reader.BaseStream.Position = 0;
                        for (int i = 0; i < PCAConstants.GPLEN; i++)
                        {
                            avgs[i] = reader.ReadSingle();
                        }
                        for (int i = 0; i < PCAConstants.GPLEN; i++)
                        {
                            for (int j = 0; j < PCAConstants.EPCALEN; j++)
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
                        int count = 0;
                        for (int i = 0; i < PCAConstants.GPLEN; i++)
                        {
                            avgs[i] = float.Parse(numbers[count++]);
                        }
                        for (int i = 0; i < PCAConstants.GPLEN; i++)
                        {
                            for (int j = 0; j < PCAConstants.PCALEN; j++)
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
            vec = PCAUtil.NormalizeVector(vec);

            for (int i = 0; i < PCAConstants.GPLEN; i++)
            {
                vec[i] -= avgs[i];
            }

            float[] result = new float[PCAConstants.EPCALEN];

            for (int desci = 0; desci < PCAConstants.EPCALEN; desci++)
            {
                float total = 0;

                for (int x = 0; x < PCAConstants.GPLEN; x++)
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
            float[] vec = new float[PCAConstants.GPLEN];

            int patchsize, iradius;
            float sine, cosine, sizeratio;

            float scale = PCAConstants.SIGMA * (float)Math.Pow(2.0, keypoint.FScale / PCAConstants.SCALES_PER_OCTAVE);

            // Sampling window size
            patchsize = (int)(PCAConstants.PATCH_MAG * scale);

            // Make odd
            patchsize = (patchsize / 2) * 2 + 1;

            // Technically a bug fix but should do the trick for now
            if (patchsize < PCAConstants.PATCH_SIZE)
            {
                patchsize = PCAConstants.PATCH_SIZE;
            }

            sizeratio = patchsize / (float)PCAConstants.PATCH_SIZE;
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
                    data[x + iradius, y + iradius, 0] = PCAUtil.GetPixelBilinearInterpolation(blurData, cpos, rpos, height, width);
                }
            }

            int count = 0;
            float x1, x2, y1, y2, gx, gy;
            for (int y = 1; y < PCAConstants.PATCH_SIZE - 1; y++)
            {
                for (int x = 1; x < PCAConstants.PATCH_SIZE - 1; x++)
                {
                    x1 = PCAUtil.GetPixelBilinearInterpolation(data, y * sizeratio, (x + 1) * sizeratio, height, width)/255;
                    x2 = PCAUtil.GetPixelBilinearInterpolation(data, y * sizeratio, (x - 1) * sizeratio, height, width)/255;
                    y1 = PCAUtil.GetPixelBilinearInterpolation(data, (y + 1) * sizeratio, x * sizeratio, height, width)/255;
                    y2 = PCAUtil.GetPixelBilinearInterpolation(data, (y - 1) * sizeratio, x * sizeratio, height, width)/255;

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

            im = PCAUtil.ScaleInitImage(im);
            List<List<Image<Gray, float>>> GOctaves = PCAUtil.BuildGaussianOctaves(im);
            PCAUtil.UpdateKeypoints(keypoints);
            ComputeLocalDescriptors(keypoints, GOctaves);
        }
    }    
}
