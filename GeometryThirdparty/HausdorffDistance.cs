using log4net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace OPS.Geometry
{
    /// <summary>
    /// Stores the distance between two meshes
    /// </summary>
    public class HausdorffDistance
    {
        private static readonly ILog logger = LogManager.GetLogger(typeof(HausdorffDistance));

        public double Min;
        public double Max;
        public double Mean;
        public double RMS;

        public HausdorffDistance(double min, double max, double mean, double rms)
        {
            this.Min = min;
            this.Max = max;
            this.Mean = mean;
            this.RMS = rms;
        }

        public static HausdorffDistance Largest(HausdorffDistance hd1, HausdorffDistance hd2)
        {
            return new HausdorffDistance(Math.Max(hd1.Min, hd2.Min), Math.Max(hd1.Max, hd2.Max), Math.Max(hd1.Mean, hd2.Mean), Math.Max(hd1.RMS, hd2.RMS));
        }

        public static HausdorffDistance ParseFromMeshLabOutput(string text)
        {
            string[] lines = text.Split('\n');
            int i = 0;
            InvalidText(i >= lines.Length, text);
            // loop through the lines until we find expected text
            while (!lines[i].Contains("Hausdorff Distance computed"))
            {
                i++;
                InvalidText(i >= lines.Length, text);
            }
            i++; // Move to next line
            InvalidText(i >= lines.Length, text);
            if (!lines[i].Contains("Sampled"))
            {
                throw new MeshLabException("Unexpected output from MeshLab HausdorffDistance");
            }
            i++; // Move to next line
            InvalidText(i >= lines.Length, text);
            string pattern = @"\s+min\s+:\s+(\d+\.\d+)\s+max\s(\d+\.\d+)\s+mean\s+:\s+(\d+\.\d+)\s+RMS\s+:\s+(\d+\.\d+)";
            Match m = Regex.Match(lines[i], pattern);
            InvalidText(m.Groups.Count != 5, text);
            double min = double.Parse(m.Groups[1].Value);
            double max = double.Parse(m.Groups[2].Value);
            double mean = double.Parse(m.Groups[3].Value);
            double rms = double.Parse(m.Groups[4].Value);
            return new HausdorffDistance(min, max, mean, rms);
        }

        public static void InvalidText(bool isInvalid, string text)
        {
            if (isInvalid)
            {
                logger.Error("Unexpected MeshLab output for HausdorffDistance");
                logger.Error(text);
                throw new MeshLabException("Unexpected MeshLab output for HausdorffDistance");
            }
        }
    }
}
