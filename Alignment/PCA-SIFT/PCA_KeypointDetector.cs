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
        const int SCALES_PER_OCTAVE = 5;
        const int MAX_OCTAVES = 14;
        static int DOUBLE_BASE_IMAGE_SIZE = 0;//1;
        const int GPLEN = (PATCHSIZE - 2) * (PATCHSIZE - 2) * 2;
        const int PCALEN = 36;
        const int EPCALEN = 36;
        float[] avgs = new float[GPLEN];
        float[,] eigs = new float[GPLEN, PCALEN];

        /// <summary>
        /// Initializes a new instance of the <see cref="T:OPS.Alignment.PCA_KeypointDetector"/> class.
        /// </summary>
        /// <param name="file">File containing mean and eigenspace computed from training set.</param>
        public PCA_KeypointDetector(string file)
        {
            if (File.Exists(file))
            {
                using (BinaryReader reader = new BinaryReader(new FileStream(file, FileMode.Open)))
                {
                    reader.BaseStream.Position = 0;
                    Debug.WriteLine("Reading averages.");
                    for (int i = 0; i < GPLEN; i++)
                    {
                        avgs[i] = reader.ReadSingle();
                    }

                    Debug.WriteLine("Reading pca vector {0}x{1}", GPLEN, PCALEN);
                    for (int i = 0; i < GPLEN; i++)
                    {
                        for (int j = 0; j < PCALEN; j++)
                        {

                            eigs[i, j] = reader.ReadSingle();
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
                result[desci] = 0;

                for (int x = 0; x < GPLEN; x++)
                {
                    result[desci] += eigs[x, desci] * vec[x];
                }
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

            sine = (float)Math.Sin(keypoint.Angle);
            cosine = (float)Math.Cos(keypoint.Angle);

            iradius = patchsize / 2;

            float cpos, rpos;
            for (int y = -iradius; y <= iradius; y++)
            {
                for (int x = -iradius; x <= iradius; x++)
                {
                    cpos = (cosine * x  + sine * y) + keypoint.SX;
                    rpos = (-sine * x  + cosine * y) + keypoint.SY;
                    // not sure about this order of coordinates either
                    patch[y + iradius, x + iradius] = new Gray(GetPixelBilinearInterpolation(blur, cpos, rpos));
                }
            }

            int count = 0;
            float x1, x2, y1, y2, gx, gy;
            for (int y = 1; y< PATCHSIZE - 1; y++)
            {
                for (int x = 1; x < PATCHSIZE - 1; x++)
                {
                    x1 = (float)patch[y * (int)sizeratio, (x + 1) * (int)sizeratio].Intensity;
                    x2 = (float)patch[y * (int)sizeratio, (x - 1) * (int)sizeratio].Intensity;
                    y1 = (float)patch[(y + 1) * (int)sizeratio, x * (int)sizeratio].Intensity;
                    y2 = (float)patch[(y - 1) * (int)sizeratio, x * (int)sizeratio].Intensity;

                    gx = x1 - x2;
                    gy = y1 - y2;

                    vec[count] = gx;
                    vec[count + 1] = gy;

                    count += 2;
                }
            }
            return vec;
        }

        /// <summary>
        /// Computes local descriptors for a set of keypoints given their corresponding Gaussian octaves.
        /// </summary>
        /// <param name="keypoints">list of <see cref="T:OPS.Alignment.PCA_Keypoint"/> instances</param>
        /// <param name="octaves">List of Guassian scales calculated for each octave.</param>
        void ComputeLocalDescriptors(List<PCA_SIFTFeature> keypoints, List<List<Image<Gray, float>>> octaves)
        {
            Parallel.For(0, keypoints.Count(), i =>
            {
                PCA_SIFTFeature key = keypoints[i];
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
            dst = new Image<Gray, float>(image.Width, image.Height);
            double sigma = Math.Sqrt(SIGMA * SIGMA - INIT_SIGMA * INIT_SIGMA);
            CvInvoke.GaussianBlur(image, dst, Size.Empty, sigma);
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

            //Debug.WriteLine(string.Format("buildGaussianScales: building scales of dimension ({0},{1})", image.Width, image.Height));

            GScales.Add(image.Clone());

            for (int i = 1; i < SCALES_PER_OCTAVE + 3; i++)
            {
                Image<Gray, float> dst = new Image<Gray, float>(image.Width, image.Height);

                double sigma1 = Math.Pow(k, i - 1) * SIGMA;
                double sigma2 = Math.Pow(k, i) * SIGMA;
                double sigma = Math.Sqrt(sigma2 * sigma2 - sigma1 * sigma1);

                //Debug.WriteLine(string.Format("buildGaussianScales: Blur {0}", sigma));
                CvInvoke.GaussianBlur(GScales[GScales.Count - 1], dst, Size.Empty, sigma);
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
            int numoctaves = (int)(Math.Log(dim) / Math.Log(2.0)) - 1; // -2??

            //Debug.WriteLine(string.Format("buildGaussianOctaves: Base image dimension is {0}x{1}", image.Width, image.Height));

            numoctaves = Math.Min(numoctaves, MAX_OCTAVES);

            //Debug.WriteLine(string.Format("buildGaussianOctaves: Building {0} octaves", numoctaves));

            Image<Gray, float> imageCopy = image.Clone();

            for (int i = 0; i < numoctaves; i++)
            {
                //Debug.WriteLine(string.Format("Building octave {0} of dimension ({1},{2})", i, imageCopy.Width, imageCopy.Height));
                // Build Gaussian scales
                List<Image<Gray, float>> scales = BuildGaussianScales(imageCopy);
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
            keypoint.Patch = new Image<Gray, float>(windowsize, windowsize);

            sine = (float)Math.Sin(keypoint.Angle);
            cosine = (float)Math.Cos(keypoint.Angle);

            iradius = windowsize / 2;

            float cpos, rpos;
            for (int y = -iradius; y <= iradius; y++)
            {
                for (int x = -iradius; x <= iradius; x++)
                {
                    cpos = (cosine * x * sizeratio + sine * y * sizeratio) + keypoint.SX;
                    rpos = (-sine * x * sizeratio + cosine * y * sizeratio) + keypoint.SY;
                    // not sure about this order of coordinates either lol
                    keypoint.Patch[y + iradius, x + iradius] = new Gray(GetPixelBilinearInterpolation(blur, cpos, rpos));
                }
            }


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
        static float GetPixelBilinearInterpolation(Image<Gray, float> image, float col, float row)
        {
            int irow, icol;
            float rfrac, cfrac;
            float row1 = 0, row2 = 0;

            irow = (int)row;
            icol = (int)col;

            if (irow < 0 || irow >= image.Height || icol < 0 || icol >= image.Width) { return 0; }

            row = Math.Min(row, image.Height - 1);
            col = Math.Min(col, image.Width - 1);

            rfrac = (float)1.0 - (row - irow); // casting may be in wrong area
            cfrac = (float)1.0 - (col - icol); // same problem as above

            if (cfrac < 1)
            {
                row1 = cfrac * (float)image[irow, icol].Intensity + (1.0f - cfrac) * (float)image[irow, icol + 1].Intensity;
            }
            else
            {
                row1 = (float)image[irow, icol].Intensity;
            }

            if (rfrac < 1)
            {
                if (cfrac < 1)
                {
                    row1 = cfrac * (float)image[irow + 1, icol].Intensity + (1.0f - cfrac) * (float)image[irow + 1, icol + 1].Intensity;
                }
                else
                {
                    row2 = (float)image[irow + 1, icol].Intensity;
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
        public static void WritePatchesToFile(List<PCA_Keypoint> keys, string filename, int patchsize)
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
                    PCA_Keypoint key = keys[i];
                    if (DOUBLE_BASE_IMAGE_SIZE == 1)
                    {
                        writer.Write(key.Y / 2);
                        writer.Write(key.X / 2);
                        writer.Write(key.GScale);
                        writer.Write(key.Angle);
                    }
                    else
                    {
                        writer.Write(key.Y);
                        writer.Write(key.X);
                        writer.Write(key.GScale);
                        writer.Write(key.Angle);
                    }
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

            for (int i = 0; i < keypoints.Count; i++)
            {
                PCA_SIFTFeature k = keypoints[i];

                double tmp = Math.Log((double)k.GScale / SIGMA) / log2 + 1.0;
                k.Octave = (int)tmp;
                k.FScale = (float)((tmp - k.Octave) * SCALES_PER_OCTAVE);
                k.Scale = (int)Math.Round(k.FScale);
                if (float.IsNaN(k.Octave) || float.IsNaN(k.FScale) || float.IsNaN(k.Scale)) {
                    Trace.WriteLine("NaN in updating keypoint params :(");
                }

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
                    k.Location.X *= 2;
                    k.Location.Y *= 2;
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
        public void ProjectKeypoints(Imaging.Image image, List<PCA_SIFTFeature> keypoints)
        {
            Image<Gray, float> im = ScaleInitImage(image.ToEmguGrayscale().Convert<Gray, float>());
            List<List<Image<Gray, float>>> GOctaves = BuildGaussianOctaves(im);
            UpdateKeypoints(keypoints);
            ComputeLocalPatches(keypoints, GOctaves, PATCHSIZE);
            ComputeLocalDescriptors(keypoints, GOctaves);
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
                total += vector[i];
            }

            if (total == 0)
            {
                return;
            }

            total /= vector.Length;

            for (int i = 0; i < vector.Length; i++)
            {
                vector[i] /= total * 100f; // not sure if this is necessary.
            }
        }

        /// <summary>
        /// Calculates list of gradients from a list of keypoints.
        /// </summary>
        /// <param name="keypoints">Input keypoints.</param>
        /// <returns>List of concatenated horizontal and vertical gradients.</returns>
        public static List<float[]> GetGradients(List<PCA_Keypoint> keypoints)
        {
            List<float[]> result = new List<float[]>();

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
                Debug.WriteLine((float)keypoints.Count());
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
    }
}
