using Emgu.CV;
using Emgu.CV.Structure;
using Microsoft.Xna.Framework;
using JPLOPS.Imaging;
using JPLOPS.Imaging.Emgu;
using System.Collections.Generic;
using System.IO;
using System.Linq;


namespace JPLOPS.ImageFeatures
{

    /// <summary>
    /// Support for read and writing PCA sift features for debugging
    /// </summary>
    public class PCASIFTIO
    {
        public static void WriteSIFTFeatures(Image image, string filename, Image mask = null, int numFeatures = 0, int octaveLayers = 3, float contrastThreshold = 0.04f, float edgeThreshold = 10f, float sigma = 1.6f)
        {
            Emgu.CV.XFeatures2D.SIFT sift = new Emgu.CV.XFeatures2D.SIFT(numFeatures, octaveLayers, contrastThreshold, edgeThreshold, sigma);
            var emguImg = image.ToEmguGrayscale();
            Image<Gray, byte> emguMask = (mask != null) ? (mask.ToEmguGrayscale()) : null;

            using (StreamWriter writer = new StreamWriter(new FileStream(filename, FileMode.Create)))
            {
                MKeyPoint[] kps = sift.Detect(emguImg, emguMask);
                writer.WriteLine("{0} {1}", kps.Length, 128);
                foreach (var kp in kps)
                {
                    writer.WriteLine("{0} {1} {2} {3}", string.Format("{0:0.0000000000}", kp.Point.Y), string.Format("{0:0.0000000000}", kp.Point.X),
                                                        string.Format("{0:0.0000000000}", kp.Size), string.Format("{0:0.0000000000000}", MathHelper.ToRadians(kp.Angle)));

                    // features are written in rows of 20
                    for (int i = 0; i < 120; i++)
                    {
                        writer.Write(" 0");
                        if (i % 20 == 0)
                        {
                            writer.WriteLine();
                        }
                    }
                    for (int k = 0; k < 8; k++)
                    {
                        writer.Write(" 0");
                    }
                    writer.WriteLine();
                }
            }
        }

        public static List<PCASIFTFeature> ReadKeysFromFile(string filename)
        {
            List<PCASIFTFeature> result = new List<PCASIFTFeature>();
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
                    float[] descriptor = new float[PCAConstants.PCALEN];
                    for (int j = 0; j < 128; j++)
                    {
                        if (j > 35)
                        {
                            count++;
                            continue;
                        }
                        descriptor[j] = float.Parse(numbers[count++]);
                    }
                    result.Add(new PCASIFTFeature(location, size, angle, 0, 0, new PCASIFTDescriptor(descriptor)));
                }
            }
            return result;
        }

        /// <summary>
        /// Writes gradients to file.
        /// </summary>
        /// <param name="keypoints">Keypoints whose gradients are to be written.</param>
        /// <param name="filename">Filename of place where gradients are to be saved.</param>
        public static void WriteGradientsToFile(List<PCASIFTFeature> keypoints, string filename)
        {
            using (BinaryWriter writer = new BinaryWriter(new FileStream(filename, FileMode.Append)))
            {
                writer.Write((float)keypoints.Count());
                for (int i = 0; i < keypoints.Count(); i++)
                {
                    int patchsize = keypoints[i].Patch.Width;
                    int gsize = (patchsize - 2) * (patchsize - 2) * 2;
                    float[] vec = new float[gsize];
                    int count = 0;
                    float x1, x2, y1, y2, gx, gy;
                    PCASIFTFeature key = keypoints[i];

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
                    vec = PCAUtil.NormalizeVector(vec);
                    for (int z = 0; z < gsize; z++)
                    {
                        writer.Write(vec[z]);
                    }
                }
            }
        }

        /// <summary>
        /// Save descriptors to file, for debugging purposes.
        /// </summary>
        /// <param name="keypoints">Keypoints to write to file.</param>
        /// <param name="filename">Location to save written file.</param>
        public static void WriteDescriptorsToFile(List<PCASIFTFeature> keypoints, string filename)
        {
            using (StreamWriter writer = new StreamWriter(new FileStream(filename, FileMode.Create)))
            {
                // mean should be of length 3042
                writer.WriteLine("{0} {1}", keypoints.Count, 36);
                for (int a = 0; a < keypoints.Count; a++)
                {
                    PCASIFTFeature key = keypoints[a];
                    writer.WriteLine("{0} {1} {2} {3}", string.Format("{0:0.00}", key.Location.Y), string.Format("{0:0.00}", key.Location.X),
                        string.Format("{0:0.000}", key.Size), string.Format("{0:0.000}", key.Angle));
                    var data = ((PCASIFTDescriptor)key.Descriptor).Data;
                    for (int j = 0; j < 36; j++)
                    {
                        if (j % 12 == 0) writer.WriteLine();
                        writer.Write(" " + string.Format("{0:0.}", data[j]));
                    }
                }
            }
        }
               
        /// <summary>
        /// Writes keypoints and their associated patches to file.
        /// </summary>
        /// <param name="keys">List of keypoints with patches.</param>
        /// <param name="filename">Filename of place where the patches are to be saved.</param>
        /// <param name="patchsize">Height and width of patch.</param>
        public static void WritePatchesToFile(List<PCASIFTFeature> keys, string filename, int patchsize)
        {
            using (BinaryWriter writer = new BinaryWriter(new FileStream(filename, FileMode.Create)))
            {
                // number of keypoints and vector length
                writer.Write((float)keys.Count());
                writer.Write((float)patchsize * patchsize);
                for (int i = 0; i < keys.Count; i++)
                {
                    PCASIFTFeature key = keys[i];
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
        }
    }
}
