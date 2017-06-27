using System;
using System.Collections.Generic;
using System.Linq;
using Emgu.CV;
using Emgu.CV.Structure;
using Emgu.CV.CvEnum;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using OPS.Imaging.Emgu;

namespace OPS.Alignment
{
    /// <summary>
    /// PCA Keypoint Projector class.
    /// </summary>
    public class PCAKeypointProjector
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
        const int KERNEL_DIM = 11;
        float[] avgs = new float[GPLEN];
        float[,] eigs = new float[EPCALEN, GPLEN];
        static int counter = 0;

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
            PCASIFTUtil.NormalizeVector(vec);

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
            keypoint.Descriptor = new PCA_SIFTDescriptor(result); 
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
                    data[x + iradius, y + iradius, 0] = GetPixelBilinearInterpolation(blurData, cpos, rpos, height, width);
                }
            }

            int count = 0;
            float x1, x2, y1, y2, gx, gy;
            for (int y = 1; y < PATCHSIZE - 1; y++)
            {
                for (int x = 1; x < PATCHSIZE - 1; x++)
                {
                    x1 = GetPixelBilinearInterpolation(data, y * sizeratio, (x + 1) * sizeratio, height, width)/255;
                    x2 = GetPixelBilinearInterpolation(data, y * sizeratio, (x - 1) * sizeratio, height, width)/255;
                    y1 = GetPixelBilinearInterpolation(data, (y + 1) * sizeratio, x * sizeratio, height, width)/255;
                    y2 = GetPixelBilinearInterpolation(data, (y - 1) * sizeratio, x * sizeratio, height, width)/255;

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
        /// Scales and blurs input image to make base image for Gaussian pyramid.
        /// </summary>
        /// <param name="image">Input image.</param>
        /// <returns>Base image for Gaussian pyramid.</returns>
        static Image<Gray, float> ScaleInitImage(Image<Gray, float> image)
        {
            Image<Gray, float> dst;
            if (DOUBLE_BASE_IMAGE_SIZE == 1)
            {
                Image<Gray, float> img = image.Clone().Resize(2, Inter.Area);
                dst = new Image<Gray, float>(img.Width, img.Height);
                float sigma = (float)Math.Sqrt(SIGMA * SIGMA - 4 * INIT_SIGMA * INIT_SIGMA);
                dst = img.SmoothGaussian(KERNEL_DIM, KERNEL_DIM, SIGMA, SIGMA);
            }
            else
            {
                dst = new Image<Gray, float>(image.Width, image.Height);
                float sigma = (float)Math.Sqrt(SIGMA * SIGMA - INIT_SIGMA * INIT_SIGMA);
                dst = image.SmoothGaussian(KERNEL_DIM, KERNEL_DIM, SIGMA, INIT_SIGMA);
            }
            return dst;
        }

        /// <summary>
        /// Computes a Gaussian pyramid for a specific octave.
        /// </summary>
        /// <param name="image">Input image.</param>
        /// <returns>List of scales for the octave.</returns>
        static List<Image<Gray, float>> BuildGaussianScales(Image<Gray, float> image)
        {
            List<Image<Gray, float>> GScales = new List<Image<Gray, float>>();
            double k = Math.Pow(2, 1.0 / ((float)SCALES_PER_OCTAVE));

            GScales.Add(image.Clone());

            for (int i = 1; i < SCALES_PER_OCTAVE + 3; i++)
            {
                Image<Gray, float> dst = new Image<Gray, float>(image.Width, image.Height);

                double sigma1 = Math.Pow(k, i - 1) * SIGMA;
                double sigma2 = Math.Pow(k, i) * SIGMA;
                double sigma = Math.Sqrt(sigma2 * sigma2 - sigma1 * sigma1);
                int kernelDim = (int)Math.Max(3, 2 * 4 * sigma + 1f);

                kernelDim = kernelDim % 2 == 0 ? kernelDim + 1 : kernelDim;
                dst = GScales[GScales.Count - 1].SmoothGaussian(kernelDim, kernelDim, sigma1, sigma2);
                GScales.Add(dst);
            }
            return GScales;
        }

        /// <summary>
        /// Computes a Gaussian pyramid for a specific image.
        /// </summary>
        /// <param name="image"></param>
        /// <returns>List of scales for each octave, as a list.</returns>
        static List<List<Image<Gray, float>>> BuildGaussianOctaves(Image<Gray, float> image) // not void, find right type
        {
            List<List<Image<Gray, float>>> octaves = new List<List<Image<Gray, float>>>();
            int dim = Math.Min(image.Height, image.Width);
            int numoctaves = (int)(Math.Log(dim) / Math.Log(2.0)) - 2;// ????????
            if (dim < 1000) numoctaves += 1;

            numoctaves = Math.Min(numoctaves, MAX_OCTAVES);

            Image<Gray, float> imageCopy = image.Clone();

            for (int i = 0; i < numoctaves; i++)
            {
                // Build Gaussian scales
                List<Image<Gray, float>> scales = BuildGaussianScales(imageCopy);
                octaves.Add(scales);

                // Halve the image 
                Image<Gray, float> halvedImageCopy = scales[SCALES_PER_OCTAVE].Clone().Resize(0.5, Inter.Area);
                imageCopy = halvedImageCopy;
            }

            return octaves;
        }

        /// <summary>
        /// Creates an image patch for a given keypoint of an image.
        /// </summary>
        /// <param name="keypoint">Keypoint of interest.</param>
        /// <param name="blur">Source image.</param>
        /// <param name="windowsize">Height and width of patch.</param>
        static void MakeLocalPatch(PCASIFTFeature keypoint, Image<Gray, float> blur, int windowsize)
        {
            int patchsize, iradius;
            double sine, cosine, sizeratio;
            float scale = SIGMA * (float)Math.Pow(2.0, keypoint.FScale / SCALES_PER_OCTAVE);

            // Sampling window size
            patchsize = (int)(PATCHMAG * scale);

            // Make odd
            patchsize = (patchsize / 2) * 2 + 1;

            // Technically a bug fix but should do the trick for now
            if (patchsize < PATCHSIZE) patchsize = PATCHSIZE;

            sizeratio = patchsize / (float)PATCHSIZE;
            keypoint.Patch = new Image<Gray, float>(windowsize, windowsize);
            float[,,] data = keypoint.Patch.Data;

            sine = (float)Math.Sin(keypoint.Angle);
            cosine = (float)Math.Cos(keypoint.Angle);

            iradius = windowsize / 2;
            float[,,] blurData = blur.Data;
            int height = blur.Height;
            int width = blur.Width;

            double cpos, rpos;
            for (int y = -iradius; y <= iradius; y++)
            {
                for (int x = -iradius; x <= iradius; x++)
                {
                    cpos = (float)(cosine * x * sizeratio + sine * y * sizeratio) + keypoint.SX;
                    rpos = (float)(-sine * x * sizeratio + cosine * y * sizeratio) + keypoint.SY;
                    data[x + iradius, y + iradius, 0] = GetPixelBilinearInterpolation(blurData, cpos, rpos, height, width);
                }
            }
        }

        /// <summary>
        /// Creates an image patch for all keypoints of an image.
        /// </summary>
        /// <param name="keypoints">List of keypoints</param>
        /// <param name="octaves">Calculated Gaussian pyramids for all octaves.</param>
        /// <param name="patchsize">Height and width of patch.</param>
        static void ComputeLocalPatches(List<PCASIFTFeature> keypoints, List<List<Image<Gray, float>>> octaves, int patchsize)
        {
            for (int i = 0; i < keypoints.Count; i++)
            {
                PCASIFTFeature key = keypoints[i];

                Debug.Assert(key.Octave >= 0 && key.Octave < octaves.Count);
                Debug.Assert(key.Scale >= 0 && key.Scale < octaves[key.Octave].Count);

                MakeLocalPatch(key, octaves[key.Octave][key.Scale], patchsize);
            }
        }

        /// <summary>
        /// Given an image and pixel location, calculates approximate intensity using bilinear interpolation.
        /// </summary>
        /// <param name="image"></param>
        /// <param name="col"></param>
        /// <param name="row"></param>
        /// <returns></returns>
        static float GetPixelBilinearInterpolation(float[,,] data, double col, double row, int height, int width)
        {
            int irow, icol;
            float rfrac, cfrac;
            float row1 = 0, row2 = 0;

            irow = (int)row;
            icol = (int)col;

            if (irow < 0 || irow >= height || icol < 0 || icol >= width) { return 0; }

            row = Math.Min(row, height - 1);
            col = Math.Min(col, width - 1);

            rfrac = (float) (1.0 - (row - irow));
            cfrac = (float)(1.0 - (col - icol));
            
            if (cfrac < 1)
            {
                row1 = cfrac * data[irow, icol, 0] + (1.0f - cfrac) * data[irow, icol + 1, 0];
            }
            else
            {
                row1 = data[irow, icol, 0];
            }

            if (rfrac < 1)
            {
                if (cfrac < 1)
                {
                    row2 = cfrac * data[irow + 1, icol, 0] + (1.0f - cfrac) * data[irow + 1, icol + 1, 0];
                }
                else
                {
                    row2 = data[irow + 1, icol, 0];
                }
            }
            return rfrac * row1 + (1f - rfrac) * row2;
        }

        /// <summary>
        /// Gathers patches of size patchsize x patchsize for all keypoints of a given image.
        /// </summary>
        /// <param name="image">Input image.</param>
        /// <param name="keypoints">Keypoints detected with SIFT.</param>
        /// <param name="patchsize">Height and width of patch.</param>
        /// <returns></returns>
        public static List<PCASIFTFeature> GetPatches(Image<Gray, float> image, List<PCASIFTFeature> keypoints, int patchsize)
        {
            // 1. Scale image to create base of Gaussian pyramid
            image = ScaleInitImage(image);

            // 2. Build Gaussian octaves
            List<List<Image<Gray, float>>> octaves = BuildGaussianOctaves(image);

            // 3. Update all keypoint parameters
            UpdateKeypoints(keypoints);

            // 4. Compute local patches
            ComputeLocalPatches(keypoints, octaves, patchsize);

            return keypoints;
        }

        /// <summary>
        /// Updates the fields of given keypoints such that patches may be computed.
        /// </summary>
        /// <param name="keypoints">Input keypoints.</param>
        static void UpdateKeypoints(List<PCASIFTFeature> keypoints)
        {
            float log2 = (float)Math.Log(2);
            for (int i = 0; i < keypoints.Count; i++)
            {
                PCASIFTFeature k = keypoints[i];

                double tmp = Math.Log((double)k.GScale / SIGMA) / log2 + 1.0;
                k.Octave = (int)tmp;
                k.FScale = (float)((tmp - k.Octave) * SCALES_PER_OCTAVE);
                k.Scale = (int)Math.Round(k.FScale);

                if (k.Scale == 0 && k.Octave > 0)
                {
                    k.Scale = SCALES_PER_OCTAVE;
                    k.Octave -= 1;
                    k.FScale += SCALES_PER_OCTAVE;
                }

                k.SX = (float)(k.Location.X / Math.Pow(2.0, k.Octave));
                k.SY = (float)(k.Location.Y / Math.Pow(2.0, k.Octave));

                if (DOUBLE_BASE_IMAGE_SIZE == 1)
                {
                    //k.Location.X *= 2;
                    //k.Location.Y *= 2; // This doesn't need to change.
                    k.SX *= 2;
                    k.SY *= 2;
                }
            }
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

            im = ScaleInitImage(im);
            List<List<Image<Gray, float>>> GOctaves = BuildGaussianOctaves(im);
            UpdateKeypoints(keypoints);
            ComputeLocalDescriptors(keypoints, GOctaves);
        }
    }    
}
