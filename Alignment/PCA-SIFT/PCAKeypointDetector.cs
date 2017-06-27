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
    /// PCA Keypoint Detector class.
    /// </summary>
    public class PCA_KeypointDetector
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
        public PCA_KeypointDetector(string file, bool textFile = false)
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
		void MakeKeypointPCA(PCA_SIFTFeature keypoint, Image<Gray, float> blur)
        {
            float[] vec = KeypointPatchVector(keypoint, blur);
            NormalizeVector(vec);

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
        float[] KeypointPatchVector(PCA_SIFTFeature keypoint, Image<Gray, float> blur)
        {
            //Debug.Assert(keypoint != null);
            //Debug.Assert(blur != null);
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
                    rpos = (-sine * x  + cosine * y) + keypoint.SY;
                    // not sure about this order of coordinates either
                    data[x + iradius, y + iradius, 0] = GetPixelBilinearInterpolation(blurData, cpos, rpos, height, width);
                }
            }

            int count = 0;
            int diff_count = 0;
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
                   // Debug.WriteLine("count: {0} | x1: {1}, x2: {2}, y1: {3}, y2: {4}", diff_count++, x1, x2, y1, y2);
                   
                }
            }

           // Debug.WriteLine("x: {0}, y: {1}, gscale: {2}, ori: {3}", keypoint.Location.X, keypoint.Location.Y, keypoint.GScale, keypoint.Angle);
            
            return vec;
        }

        /// <summary>
        /// Computes local descriptors for a set of keypoints given their corresponding Gaussian octaves.
        /// </summary>
        /// <param name="keypoints">list of <see cref="T:OPS.Alignment.PCA_Keypoint"/> instances</param>
        /// <param name="octaves">List of Guassian scales calculated for each octave.</param>
        void ComputeLocalDescriptors(List<PCA_SIFTFeature> keypoints, List<List<Image<Gray, float>>> octaves)
        {
            //Parallel.For(0, keypoints.Count(), i =>
            //{
            for (int i = 0; i < keypoints.Count; i++) {
                PCA_SIFTFeature key = keypoints[i];
                MakeKeypointPCA(keypoints[i], octaves[key.Octave][key.Scale]);
            }
            //});
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
                Image<Gray, float> img = image.Clone().Resize(2, Inter.Linear);
                dst = new Image<Gray, float>(img.Width, img.Height);
                float sigma = (float)Math.Sqrt(SIGMA * SIGMA - 4 * INIT_SIGMA * INIT_SIGMA);
                //dst = BlurImage(img, sigma);
                dst = img.SmoothGaussian(KERNEL_DIM, KERNEL_DIM, SIGMA, SIGMA);
            }
            else
            {
                dst = new Image<Gray, float>(image.Width, image.Height);
                float sigma = (float)Math.Sqrt(SIGMA * SIGMA - INIT_SIGMA * INIT_SIGMA);
                //dst = BlurImage(image, sigma);
                dst = image.SmoothGaussian(KERNEL_DIM, KERNEL_DIM, SIGMA, INIT_SIGMA);
            }
            return dst;
        }

        /// <summary>
        /// Computes a Gaussian pyramid for a specific octave.
        /// </summary>
        /// <param name="image">Input image.</param>
        /// <returns>List of scales for the octave.</returns>
        static List<Image<Gray, float>> BuildGaussianScales(Image<Gray, float> image, int octave)
        {
            List<Image<Gray, float>> GScales = new List<Image<Gray, float>>();
            double k = Math.Pow(2, 1.0 / ((float)SCALES_PER_OCTAVE));

           // Debug.WriteLine(string.Format("buildGaussianScales: building scales of dimension ({0},{1})", image.Width, image.Height));

            GScales.Add(image.Clone());

            for (int i = 1; i < SCALES_PER_OCTAVE + 3; i++)
            {
                Image<Gray, float> dst = new Image<Gray, float>(image.Width, image.Height);

                double sigma1 = Math.Pow(k, i - 1) * SIGMA;
                double sigma2 = Math.Pow(k, i) * SIGMA;
                double sigma = Math.Sqrt(sigma2 * sigma2 - sigma1 * sigma1);

                //Debug.WriteLine(string.Format("buildGaussianScales: Blur {0}", sigma));
                int kernelDim = (int)Math.Max(3, 2 * 4 * sigma + 1f);
                kernelDim = kernelDim % 2 == 0 ? kernelDim + 1 : kernelDim;
                //Debug.WriteLine("kernel dim: {0}", kernelDim);

                try
                {
                    //dst = BlurImage(GScales[GScales.Count - 1], (float)sigma);
                    dst = GScales[GScales.Count - 1].SmoothGaussian(kernelDim, kernelDim, sigma1, sigma2);
                    //dst.Save("C:\\Users\\charchut\\Pictures\\junk\\CS\\oct_"+octave+"_"+ i +".pgm");
                } catch (Exception e)
                {
                    Debug.WriteLine(e, e.StackTrace);
                }
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
            int numoctaves = (int)(Math.Log(dim) / Math.Log(2.0)) - 2; // -2??

           // Debug.WriteLine(string.Format("BuildGaussianOctaves: Base image dimension is {0}x{1}", image.Width, image.Height));

            numoctaves = Math.Min(numoctaves, MAX_OCTAVES);

            //Debug.WriteLine(string.Format("buildGaussianOctaves: Building {0} octaves", numoctaves));

            Image<Gray, float> imageCopy = image.Clone();

            for (int i = 0; i < numoctaves; i++)
            {
                //Debug.WriteLine(string.Format("Building octave {0} of dimension ({1},{2})", i, imageCopy.Width, imageCopy.Height));
                // Build Gaussian scales
                List<Image<Gray, float>> scales = BuildGaussianScales(imageCopy, i);
                octaves.Add(scales);

                // Halve the image 
                //Image<Gray, byte> halvedImageCopy = new Image<Gray, byte>(scales[SCALES_PER_OCTAVE].Width / 2,
                //                                                          scales[SCALES_PER_OCTAVE].Height) / 2;
                //CvInvoke.Resize(scales[SCALES_PER_OCTAVE], halvedImageCopy, Size.Empty, 0.5, 0.5);

                Image<Gray, float> halvedImageCopy = scales[SCALES_PER_OCTAVE].Clone().Resize(0.5, Inter.Area);

                imageCopy = halvedImageCopy;
            }

            //Debug.WriteLine("octaves length: " + octaves.Count);
            return octaves;
        }

        /// <summary>
        /// Creates an image patch for a given keypoint of an image.
        /// </summary>
        /// <param name="keypoint">Keypoint of interest.</param>
        /// <param name="blur">Source image.</param>
        /// <param name="windowsize">Height and width of patch.</param>
        static void MakeLocalPatch(PCA_SIFTFeature keypoint, Image<Gray, float> blur, int windowsize)
        {
            //Debug.Assert(keypoint != null);
            //Debug.Assert(blur != null);

            int patchsize, iradius;
            double sine, cosine, sizeratio;

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
            keypoint.Patch = new Image<Gray, float>(windowsize, windowsize);
            float[,,] data = keypoint.Patch.Data;

            sine = (float)Math.Sin(keypoint.Angle);
            cosine = (float)Math.Cos(keypoint.Angle);

            iradius = windowsize / 2;
            float[,,] blurData = blur.Data;
            int height = blur.Height;
            int width = blur.Width;

            double cpos, rpos;
            //Debug.WriteLine("keypoint at ({0}, {1}", keypoint.Location.X, keypoint.Location.Y);
            for (int y = -iradius; y <= iradius; y++)
            {
                for (int x = -iradius; x <= iradius; x++)
                {

                        cpos = (float)(cosine * x * sizeratio + sine * y * sizeratio) + keypoint.SX;
                    rpos = (float)(-sine * x * sizeratio + cosine * y * sizeratio) + keypoint.SY;
                    // not sure about this order of coordinates either lol

                    data[x + iradius, y + iradius, 0] = GetPixelBilinearInterpolation(blurData, cpos, rpos, height, width);
                }
            }
            //blur.Save(@"C:\Users\charchut\Desktop\blurrrrr.png");
           // Debug.WriteLine("SX: {0}, SY: {1}", keypoint.SX, keypoint.SY);
           //keypoint.Patch.Save("C:\\Users\\charchut\\Desktop\\new_patches\\patch_" + counter++ + ".png");

        }

        /// <summary>
        /// Creates an image patch for all keypoints of an image.
        /// </summary>
        /// <param name="keypoints">List of keypoints</param>
        /// <param name="octaves">Calculated Gaussian pyramids for all octaves.</param>
        /// <param name="patchsize">Height and width of patch.</param>
        static void ComputeLocalPatches(List<PCA_SIFTFeature> keypoints, List<List<Image<Gray, float>>> octaves, int patchsize)
        {
            for (int i = 0; i < keypoints.Count; i++)
            {
                PCA_SIFTFeature key = keypoints[i];

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

            rfrac = (float) (1.0 - (row - irow)); // casting may be in wrong area
            cfrac = (float)(1.0 - (col - icol)); // same problem as above
            
            if (cfrac < 1)
            {
                row1 = cfrac * data[irow, icol, 0] + (1.0f - cfrac) * data[irow, icol + 1, 0];
                //Debug.WriteLine(data[irow, icol, 0] + " " + data[irow, icol + 1, 0]);
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
            //Debug.WriteLine(rfrac * row1 + (1f - rfrac) * row2);
            return rfrac * row1 + (1f - rfrac) * row2;
        }

        /// <summary>
        /// Gathers patches of size patchsize x patchsize for all keypoints of a given image.
        /// </summary>
        /// <param name="image">Input image.</param>
        /// <param name="keypoints">Keypoints detected with SIFT.</param>
        /// <param name="patchsize">Height and width of patch.</param>
        /// <returns></returns>
        public static List<PCA_SIFTFeature> GetPatches(Image<Gray, float> image, List<PCA_SIFTFeature> keypoints, int patchsize)
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
        /// Writes keypoints and their associated patches to file.
        /// </summary>
        /// <param name="keys">List of keypoints with patches.</param>
        /// <param name="filename">Filename of place where the patches are to be saved.</param>
        /// <param name="patchsize">Height and width of patch.</param>
        public static void WritePatchesToFile(List<PCA_SIFTFeature> keys, string filename, int patchsize)
        {
            Debug.WriteLine("Writing to " + filename);
            using (BinaryWriter writer = new BinaryWriter(new FileStream(filename, FileMode.Create)))
            {
                // number of keypoints and vector length
                writer.Write((float)keys.Count());
                writer.Write((float)patchsize * patchsize);
                Debug.WriteLine("key count: {0}, patchsize^2: {1}", keys.Count(), patchsize * patchsize);

                for (int i = 0; i < keys.Count; i++)
                {
                    PCA_SIFTFeature key = keys[i];
                    writer.Write(key.Location.Y);
                    writer.Write(key.Location.X);
                    writer.Write(key.GScale);
                    writer.Write(key.Angle);
                    for (int y = 0; y < patchsize; y++)
                    {
                        for (int x = 0; x < patchsize; x++)
                        {
                            writer.Write(keys[i].Patch[y, x].Intensity);
                        }
                    }
                }
            }
            Debug.WriteLine("Wrote to file.");
        }

        /// <summary>
        /// Reads keypoints and their associated patches from file.
        /// </summary>
        /// <param name="filename">Filename of place from where the patches are to be read.</param>
        /// <returns></returns>
        public static List<PCA_Keypoint> ReadPatchesFromFile(string filename)
        {
            Debug.WriteLine("Reading from " + filename);
            List<PCA_Keypoint> keypoints = new List<PCA_Keypoint>();
            if (File.Exists(filename))
            {
                using (BinaryReader reader = new BinaryReader(new FileStream(filename, FileMode.Open)))
                {
                    Debug.WriteLine(reader.PeekChar());
                    float keyCount = reader.ReadSingle();
                    float pcaLength = reader.ReadSingle();
                    int sqrtLen = (int)Math.Sqrt(pcaLength);

                    if (Math.Abs(sqrtLen * sqrtLen - pcaLength) > Double.Epsilon)
                    {
                        Debug.WriteLine("Invalid patch file - dimensions incorrect: {0}", pcaLength);
                    }

                    for (int i = 0; i < keyCount; i++)
                    {
                        PCA_Keypoint key = new PCA_Keypoint()
                        {
                            Y = reader.ReadSingle(),
                            X = reader.ReadSingle(),
                            GScale = reader.ReadSingle(),
                            Angle = reader.ReadSingle(),
                            Patch = new Image<Gray, float>(sqrtLen, sqrtLen)
                        };

                        //Debug.WriteLine("New point at ({0},{1}) with gScale: {2} and angle: {3}", key.X, key.Y, key.GScale, key.Angle);
                        for (int y = 0; y < sqrtLen; y++)
                        {
                            for (int x = 0; x < sqrtLen; x++)
                            {
                                float val = (float)reader.ReadDouble();
                                key.Patch[y, x] = new Gray(val);
                            }
                        }
                        keypoints.Add(key);
                    }
                }
            }
            Debug.WriteLine("Read from file.");
            return keypoints;
        }

        /// <summary>
        /// Updates the fields of given keypoints such that patches may be computed.
        /// </summary>
        /// <param name="keypoints">Input keypoints.</param>
        static void UpdateKeypoints(List<PCA_SIFTFeature> keypoints)
        {
            float log2 = (float)Math.Log(2);
            int count = 50;
            for (int i = 0; i < keypoints.Count; i++)
            {
                PCA_SIFTFeature k = keypoints[i];

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
                    //k.Location.Y *= 2;
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
        public void ProjectKeypoints(Imaging.Image image, List<PCA_SIFTFeature> keypoints, int number = 0)
        {
            Image<Gray, byte> imByte = image.ToEmguGrayscale();
            Image<Gray, float> im = imByte.Convert<Gray, float>();
            //visualizePatches(@"C:\Users\charchut\Desktop\og_patches.txt");
            
            //im = doubleImageSize(im);
            //im = fillImage(im);
            //im.Save(@"C:\Users\charchut\Desktop\test_patch.png");
            im = ScaleInitImage(im);
            List<List<Image<Gray, float>>> GOctaves = BuildGaussianOctaves(im);
            UpdateKeypoints(keypoints);
            // ComputeLocalPatches(keypoints, GOctaves, PATCHSIZE);
            ComputeLocalDescriptors(keypoints, GOctaves);
            // WriteDescriptorsToFile(keypoints, @"C:\cygwin64\home\charchut\pcasift-0.91nd\CSrock_" + number + ".pkeys");
        }

        private void WriteDescriptorsToFile(List<PCA_SIFTFeature> keypoints, string filename)
        {
            using (StreamWriter writer = new StreamWriter(new FileStream(filename, FileMode.Create)))
            {
                // mean should be of length 3042
                writer.WriteLine("{0} {1}", keypoints.Count, 36);
                for (int a = 0; a < keypoints.Count; a++)
                {
                    PCA_SIFTFeature key = keypoints[a];
                    writer.WriteLine("{0} {1} {2} {3}", string.Format("{0:0.00}", key.Location.Y), string.Format("{0:0.00}", key.Location.X),
                        string.Format("{0:0.000}", key.Size), string.Format("{0:0.000}", key.Angle));
                    var data = ((FeatureDescriptor<float>)key.Descriptor).Data;
                    for (int j = 0; j < 36; j++)
                    {
                        if (j % 12 == 0) writer.WriteLine();
                        writer.Write(" " + string.Format("{0:0.}", data[j]));
                       
                        
                    }
                }
            }
        }

        /// <summary>
        /// Normalizes a vector.
        /// </summary>
        /// <param name="vector">Normalized vector.</param>
        public static void NormalizeVector(float[] vector)
        {
            float total = 0;

            for (int i = 0; i < vector.Length; i++)
            {
                total += Math.Abs(vector[i]);
            }

            if (total == 0)
            {
                return;
            }

            total /= vector.Length;

            for (int i = 0; i < vector.Length; i++)
            {
                vector[i] /= total / 100f; // not sure if this is necessary.
            }
        }

        /// <summary>
        /// Calculates list of gradients from a list of keypoints.
        /// </summary>
        /// <param name="keypoints">Input keypoints.</param>
        /// <returns>List of concatenated horizontal and vertical gradients.</returns>`
        public static List<float[]> GetGradients(List<PCA_SIFTFeature> keypoints)
        {
            List<float[]> result = new List<float[]>();

            for (int i = 0; i < keypoints.Count(); i++)
            {
                int patchsize = keypoints[i].Patch.Width;
                int gsize = (patchsize - 2) * (patchsize - 2) * 2;
                float[] vec = new float[gsize];
                int count = 0;
                float x1, x2, y1, y2, gx, gy;
                PCA_SIFTFeature key = keypoints[i];

                for (int y = 1; y < patchsize - 1; y++)
                {
                    for (int x = 1; x < patchsize - 1; x++)
                    {
                        x1 = (float)key.Patch[x + 1, y].Intensity;
                        x2 = (float)key.Patch[x - 1, y].Intensity;
                        y1 = (float)key.Patch[x, y + 1].Intensity;
                        y2 = (float)key.Patch[x, y - 1].Intensity;

                        gx = x1 - x2;
                        gy = y1 - y2;

                        vec[count] = gx;
                        vec[count + 1] = gy;

                        count += 2;
                    }
                }
                NormalizeVector(vec);
                result.Add(vec);
            }
            return result;
        }

        /// <summary>
        /// Writes gradients to file.
        /// </summary>
        /// <param name="keypoints">Keypoints whose gradients are to be written.</param>
        /// <param name="filename">Filename of place where gradients are to be saved.</param>
        public static void WriteGradientsToFile(List<PCA_Keypoint> keypoints, string filename)
        {
            using (BinaryWriter writer = new BinaryWriter(new FileStream(filename, FileMode.Append)))
            {
                //Debug.WriteLine((float)keypoints.Count());
                writer.Write((float)keypoints.Count());
                for (int i = 0; i < keypoints.Count(); i++)
                {
                    int patchsize = keypoints[i].Patch.Width;
                    int gsize = (patchsize - 2) * (patchsize - 2) * 2;
                    float[] vec = new float[gsize];
                    int count = 0;
                    float x1, x2, y1, y2, gx, gy;
                    PCA_Keypoint key = keypoints[i];

                    for (int y = 1; y < patchsize - 1; y++)
                    {
                        for (int x = 1; x < patchsize - 1; x++)
                        {
                            x1 = (float)key.Patch[x + 1, y].Intensity;
                            x2 = (float)key.Patch[x - 1, y].Intensity;
                            y1 = (float)key.Patch[x, y + 1].Intensity;
                            y2 = (float)key.Patch[x, y - 1].Intensity;

                            gx = x1 - x2;
                            gy = y1 - y2;

                            vec[count] = gx;
                            vec[count + 1] = gy;

                            count += 2;
                        }
                    }
                    NormalizeVector(vec);
                    for (int z = 0; z < gsize; z++)
                    {
                        writer.Write(vec[z]);
                    }
                }
            }
        }



        public static List<PCA_SIFTFeature> ReadKeysFromFile(string filename)
        {
            List<PCA_SIFTFeature> result = new List<PCA_SIFTFeature>();
            using (TextReader reader = File.OpenText(filename))
            {
                string[] numbers0 = reader.ReadToEnd().Split(new char[] { '\n', ' ' });
                List<string> numbers = new List<string>();

                foreach (string num in numbers0)
                {
                    if (num != "") numbers.Add(num);
                }

                int keyCount = int.Parse(numbers[0]);
                int count = 2;

                for (int i = 0; i < keyCount; i++)
                {

                    Vector2 location = new Vector2(float.Parse(numbers[count++]), float.Parse(numbers[count++]));
                    location = new Vector2(location.Y, location.X);
                    float size = float.Parse(numbers[count++]);
                    float angle = float.Parse(numbers[count++]);
                    float[] descriptor = new float[PCALEN];
                    for (int j = 0; j < 128; j++)
                    {
                        if (j > 35)
                        {
                            count++;
                            continue;
                        }
                        descriptor[j] = float.Parse(numbers[count++]);
                    }
                    result.Add(new PCA_SIFTFeature(location, size, angle, 0, 0, new PCA_SIFTDescriptor(descriptor)));
                }
            }
            return result;
        }


        public Image<Gray, float> doubleImageSize(Image<Gray, float> image)
        {
            Image<Gray, float> res = new Image<Gray, float>(image.Width * 2, image.Height * 2);
            float[,,] data = res.Data;
            float[,,] oldData = image.Data;

            for (int j = 0; j < image.Height; j++)
            {
                for (int i = 0; i < image.Width; i++)
                {
                    data[j*2, i*2, 0] = oldData[j, i, 0];
                }
            }

            return res;
        }

        public Image<Gray, float> fillImage(Image<Gray, float> image)
        {
            Image<Gray, float> res = new Image<Gray, float>(image.Width, image.Height);
            float[,,] data = res.Data;
            float[,,] oldData = image.Data;

            for (int j = 0; j < image.Height; j++)
            {
                for (int i = 0; i < image.Width; i++)
                {
                    if (oldData[j, i, 0] == 0)
                    {
                        data[j, i, 0] = GetPixelBilinearInterpolation(oldData, i, j, image.Height, image.Width);
                    }
                    else
                    {
                        data[j, i, 0] = oldData[j, i, 0];
                    }
                }
            }

            return res;
        }


        public void visualizePatches(string filename)
        {
            Image<Gray, float> res = new Image<Gray, float>(41, 41);
            float[,,] data = res.Data;
            using (TextReader reader = File.OpenText(filename))
            {
                string[] numbers0 = reader.ReadToEnd().Split(new char[] { '\n', ' ' });
                List<string> numbers = new List<string>();

                foreach (string num in numbers0)
                {
                    if (num != "") numbers.Add(num);
                }
                int count = 2;

                for (int x = 0; x < 17185; x++)
                {
                    count += 4;
                    for (int j = 0; j < 41; j++)
                    {
                        for (int i = 0; i < 41; i++)
                        {
                            data[i, j, 0] = float.Parse(numbers[count++]) * 255;
                        }
                    }
                    res.Save(@"C:\Users\charchut\Desktop\og_patches\patch" + x + ".jpg");
                }
            }

            return;
        }

        static Image<Gray, float> BlurImage(Image<Gray, float> image, float sigma)
        {
            float[] kernel = GaussianKernel1D(sigma);
            Image<Gray, float> temp = Convolve1DWidth(kernel, image);
            Image<Gray, float> res = Convolve1DHeight(kernel, temp);
            return res;
        }

        static float[] GaussianKernel1D(float sigma)
        {
            int dim = (int)Math.Max(3f, 2 * 4 * sigma + 1f);
            if (dim % 2 == 0) dim++;
            float[] kern = new float[dim];
            float s2 = sigma * sigma;
            int c = dim / 2;
            for (int i = 0; i < (dim + 1)/2; i++)
            {
                double v = 1 / (2 * Math.PI * s2) * Math.Exp(-(i * i) / (2 * s2));
                kern[c + i] = (float)v;
                kern[c - i] = (float)v;
            }

            float sum = 0;
            for (int i = 0; i < kern.Length; i++)
                sum += kern[i];

            for (int i = 0; i < kern.Length; i++)
                kern[i] /= sum;

            return kern;
        }

        static Image<Gray, float> Convolve1DHeight(float[] kern, Image<Gray, float> src)
        {
            Image<Gray, float> destImg = new Image<Gray, float>(src.Width, src.Height);
            float[,,] dest = destImg.Data;
            for (int j = 0; j < src.Height; j++)
            {
                for (int i = 0; i < src.Width; i++)
                {
                    //printf("%d, %d\n", i, j);
                    dest[j, i, 0] = ConvolveLocHeight(kern, src, i, j);
                }
            }
            return destImg;
        }

        static float ConvolveLocHeight(float[] kernel, Image<Gray, float> src, int x, int y)
        {
            float pixel = 0;

            int cen = kernel.Length / 2;

            float[,,] data = src.Data;
            //printf("ConvolveLoc(): Applying convoluation at location (%d, %d)\n", x, y);

            for (int j = 0; j < kernel.Length; j++)
            {
                int row = y + (j - cen);

                if (row < 0)
                    row = 0;

                if (row >= src.Height)
                    row = src.Height - 1;

                float tmp = data[row, x, 0];
                pixel += kernel[j] * tmp;
            }

            if (pixel > 255)
                pixel = 255;

            return pixel;
        }

        static Image<Gray, float> Convolve1DWidth(float[] kern, Image<Gray, float> src)
        {
            Image<Gray, float> res = new Image<Gray, float>(src.Width, src.Height);
            float[,,] dst = res.Data;
            for (int j = 0; j < src.Height; j++)
            {
                for (int i = 0; i < src.Width; i++)
                {
                    //printf("%d, %d\n", i, j);
                    dst[j, i, 0] = ConvolveLocWidth(kern, src, i, j);
                }
            }
            return res;
        }

        static float ConvolveLocWidth(float[] kernel, Image<Gray, float> src, int x, int y)
        {
            float pixel = 0;

            int cen = kernel.Length / 2;

            float[,,] data = src.Data;
            //printf("ConvolveLoc(): Applying convoluation at location (%d, %d)\n", x, y);

            for (int i = 0; i < kernel.Length; i++)
            {
                int col = x + (i - cen);
                if (col < 0)
                    col = 0;
                if (col >= src.Width)
                    col = src.Width - 1;

                float tmp = data[y, col, 0];
                pixel += kernel[i] * tmp;
            }

            if (pixel > 255)
                pixel = 255;

            return pixel;
        }

    }


    
}
