using Emgu.CV;
using Emgu.CV.Structure;
using Emgu.CV.XFeatures2D;
using Microsoft.Xna.Framework;
using OPS.Imaging;
using OPS.Imaging.Emgu;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Alignment.PCASIFT;

namespace OPS.Alignment
{
    public class PCASIFTIO
    {
        public void WriteSIFTFeatures(Image image, string filename, Image mask = null, int numFeatures = 0, int octaveLayers = 3, float contrastThreshold = 0.04f, float edgeThreshold = 10f, float sigma = 1.6f)
        {
            SIFT sift = new SIFT(numFeatures, octaveLayers, contrastThreshold, edgeThreshold, sigma);
            var emguImg = image.ToEmguGrayscale();
            //var emguImg = emguImgByte.Convert<Gray, float>();
            Image<Gray, byte> emguMask = (mask != null) ? (mask.ToEmguGrayscale()) : null;

            using (StreamWriter writer = new StreamWriter(new FileStream(filename, FileMode.Create)))
            {
                MKeyPoint[] kps = sift.Detect(emguImg, emguMask);
                writer.WriteLine("{0} {1}", kps.Length, 128);
                foreach (var kp in kps)
                {
                    writer.WriteLine("{0} {1} {2} {3}", string.Format("{0:0.0000000000}", kp.Point.Y), string.Format("{0:0.0000000000}", kp.Point.X),
                                                        string.Format("{0:0.0000000000}", kp.Size), string.Format("{0:0.0000000000000}", kp.Angle / 180 * Math.PI));

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

        public void visualizePatches(string filename, string dstTemplate)
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
                    res.Save(dstTemplate + x + ".jpg");
                }
            }
            return;
        }

        public static List<PCASIFTFeature> ReadKeysFromFile(string filename)
        {
            List<PCASIFTFeature> result = new List<PCASIFTFeature>();
            int PCALEN = 36;
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
                    Util.NormalizeVector(vec);
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
        /// Reads keypoints and their associated patches from file.
        /// </summary>
        /// <param name="filename">Filename of place from where the patches are to be read.</param>
        /// <returns></returns>
        public static List<PCASIFTFeature> ReadPatchesFromFile(string filename)
        {
            Debug.WriteLine("Reading from " + filename);
            List<PCASIFTFeature> keypoints = new List<PCASIFTFeature>();
            if (File.Exists(filename))
            {
                using (BinaryReader reader = new BinaryReader(new FileStream(filename, FileMode.Open)))
                {
                    Debug.WriteLine(reader.PeekChar());
                    float keyCount = reader.ReadSingle();
                    float pcaLength = reader.ReadSingle();
                    int sqrtLen = (int)Math.Sqrt(pcaLength);

                    if (Math.Abs(sqrtLen * sqrtLen - pcaLength) > double.Epsilon)
                    {
                        Debug.WriteLine("Invalid patch file - dimensions incorrect: {0}", pcaLength);
                    }

                    for (int i = 0; i < keyCount; i++)
                    {
                        // The following raises an argument error for whatever reason.
                        //PCASIFTFeature key = new PCASIFTFeature()
                        //{
                        //    Location = new Vector2(reader.ReadSingle(), reader.ReadSingle()),
                        //    GScale = reader.ReadSingle(),
                        //    Angle = reader.ReadSingle(),
                        //    Response = 0,
                        //    Patch = new Image<Gray, float>(sqrtLen, sqrtLen)
                        //};

                        //Debug.WriteLine("New point at ({0},{1}) with gScale: {2} and angle: {3}", key.X, key.Y, key.GScale, key.Angle);
                        //for (int y = 0; y < sqrtLen; y++)
                        //{
                        //    for (int x = 0; x < sqrtLen; x++)
                        //    {
                        //        float val = (float)reader.ReadDouble();
                        //        key.Patch[y, x] = new Gray(val);
                        //    }
                        //}
                        //keypoints.Add(key);
                    }
                }
            }
            Debug.WriteLine("Read from file.");
            return keypoints;
        }
        /// <summary>
        /// Writes keypoints and their associated patches to file.
        /// </summary>
        /// <param name="keys">List of keypoints with patches.</param>
        /// <param name="filename">Filename of place where the patches are to be saved.</param>
        /// <param name="patchsize">Height and width of patch.</param>
        public static void WritePatchesToFile(List<PCASIFTFeature> keys, string filename, int patchsize)
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
            Debug.WriteLine("Wrote to file.");
        }

    }
}
