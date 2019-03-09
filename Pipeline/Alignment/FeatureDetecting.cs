using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Emgu.CV;
using Emgu.CV.Util;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Features2D;
using OPS.Util;
using OPS.Imaging;
using OPS.Imaging.Emgu;
using OPS.Alignment;

namespace OPS.Pipeline
{
    public class FeatureDetector
    {
        public enum DetectorType
        {
            SIFT,
            ASIFT,
            PCASIFT
        }

        public const DetectorType DEF_DETECTOR_TYPE = DetectorType.ASIFT;
        public const double DEF_MIN_FEATURE_SIZE = 0;
        public const double DEF_MIN_RESPONSE = -1;
        public const double DEF_MAX_RESPONSE = -1;
        public const int DEF_MAX_FEATURES = 10000;
        public const int DEF_DECIMATION = 1;
        public const int DEF_SIFT_OCTAVES = 4;
        public const int DEF_MIN_SIFT_OCTAVE = -1;
        public const int DEF_MAX_SIFT_OCTAVE = -1;

        public class Options
        {
            public DetectorType DetectorType = DEF_DETECTOR_TYPE;
            public double MinFeatureSize = DEF_MIN_FEATURE_SIZE;
            public double MinResponse = DEF_MIN_RESPONSE;
            public double MaxResponse = DEF_MAX_RESPONSE;
            public int MaxFeatures = DEF_MAX_FEATURES;
            public int Decimation = DEF_DECIMATION;
            public int SIFTOctaves = DEF_SIFT_OCTAVES;
            public int MinSIFTOctave = DEF_MIN_SIFT_OCTAVE;
            public int MaxSIFTOctave = DEF_MAX_SIFT_OCTAVE;
            public double FeaturesPerImageBucketSize = 0; //1000 is a good value, 0 to disable
            public double FeaturesPerSizeBucketSize = 0; //5 is a good value, 0 to disable
            public double FeaturesPerResponseBucketSize = 0; //0.002 is a good value, 0 to disable
            public double FeaturesPerOctaveBucketSize = 0; //1 is a good value, 0 to disable
            public double FeaturesPerLayerBucketSize = 0; //1 is a good value, 0 to disable
            public double FeaturesPerScaleBucketSize = 0; //0.1 is a good value, 0 to disable
        }

        private Histogram featuresPerImage;
        private Histogram featuresPerSize;
        private Histogram featuresPerResponse;
        private Histogram featuresPerOctave;
        private Histogram featuresPerLayer;
        private Histogram featuresPerScale;

        private readonly PipelineCore pipeline;
        private readonly Options options;

        public FeatureDetector(PipelineCore pipeline, Options options = null)
        {
            this.pipeline = pipeline;
            this.options = options ?? new Options();

            if (options.FeaturesPerImageBucketSize > 0)
            {
                featuresPerImage = new Histogram(options.FeaturesPerImageBucketSize, "images", "valid features");
            }
            if (options.FeaturesPerSizeBucketSize > 0)
            {
                featuresPerSize = new Histogram(options.FeaturesPerSizeBucketSize, "features", "diameter");
            }
            if (options.FeaturesPerResponseBucketSize > 0)
            {
                featuresPerResponse = new Histogram(options.FeaturesPerResponseBucketSize, "features", "response");
            }
            if (options.FeaturesPerOctaveBucketSize > 0)
            {
                featuresPerOctave = new Histogram(options.FeaturesPerOctaveBucketSize, "features", "octave");
            }
            if (options.FeaturesPerLayerBucketSize > 0)
            {
                featuresPerLayer = new Histogram(options.FeaturesPerLayerBucketSize, "features", "layer");
            }
            if (options.FeaturesPerScaleBucketSize > 0)
            {
                featuresPerScale = new Histogram(options.FeaturesPerScaleBucketSize, "features", "scale");
            }
        }

        public ImageFeature[] Detect(Image img, Image mask)
        {
            if (options.Decimation > 1)
            {
                img = img.Decimated(options.Decimation);
                mask = mask.Decimated(options.Decimation);
            }

            IFeatureDetector detector = null;
            switch (options.DetectorType)
            {
                case DetectorType.SIFT:
                {
                    detector = new SIFTDetector() { OctaveLayers = options.SIFTOctaves };
                    break;
                }
                case DetectorType.ASIFT:
                {
                    detector = new ASIFTDetector() { OctaveLayers = options.SIFTOctaves };
                    break;
                }
                case DetectorType.PCASIFT:
                {
                    detector = new PCASIFTDetector() { OctaveLayers = options.SIFTOctaves };
                    break;
                }
            }

            var rawFeatures = detector.Detect(img, mask).Cast<SIFTFeature>();
            var features = FilterInvalid(rawFeatures, img, mask).ToArray();

            if (pipeline.Options.Debug)
            {
                pipeline.LogInfo("min size {0}, max size {1}",
                                 features.Select(f => f.Size * options.Decimation).Min(),
                                 features.Select(f => f.Size * options.Decimation).Max());
                pipeline.LogInfo("min response {0}, max response {1}",
                                 features.Select(f => f.Response).Min(), features.Select(f => f.Response).Max());
                pipeline.LogInfo("min octave {0}, max octave {1}",
                                 features.Select(f => f.Octave).Min(), features.Select(f => f.Octave).Max());
            }

            features = features
                .Where(f => f.Size * options.Decimation >= options.MinFeatureSize)
                .Where(f => options.MinResponse < 0 || f.Response >= options.MinResponse)
                .Where(f => options.MaxResponse < 0 || f.Response <= options.MaxResponse)
                .Where(f => options.MinSIFTOctave < 0 || f.Octave >= options.MinSIFTOctave)
                .Where(f => options.MaxSIFTOctave < 0 || f.Octave <= options.MaxSIFTOctave)
                .OrderByDescending(f => f.Response)
                .Take(options.MaxFeatures)
                .ToArray();

            //important: only add descriptors now that we've culled down the features and eliminated bad ones
            //this can save quite a bit of time
            //but also we have seen crashes in the emgucv code to collect SIFT feature descriptors
            //and computing them here hopefully limits the impact of that
            detector.AddDescriptors(img, features);

            if (options.Decimation > 1)
            {
                foreach (var feat in features)
                {
                    feat.Size *= options.Decimation;
                    feat.Location *= options.Decimation;
                }
            }

            Tally(features);

            return features;
        }

        public DetectedFeatures Detect(string imageUrl, string roverMaskUrl, string projectName,
                                       string productPath, int border = FeatureDetecting.DEF_MASK_BORDER)
        {
            var observationName = StringHelper.GetLastUrlPathSegment(imageUrl, stripExtension: true);
            try
            {
                var img = pipeline.LoadImage(imageUrl);
                var mask = FeatureDetecting.MakeMask(pipeline, roverMaskUrl, img, observationName, border);
                return new DetectedFeatures() { ImageUrl = imageUrl, Features = Detect(img, mask) };
            }
            catch (Exception ex)
            {
                pipeline.LogError("failed to detect {0} features for {1}: {2}",
                                  options.DetectorType, observationName, ex.ToString());
                return null;
            }
        }

        public void Tally(ImageFeature[] features)
        {
            if (featuresPerImage != null)
            {
                featuresPerImage.Add(features.Length);
            }
            void tally(Histogram histogram, Func<SIFTFeature, double> getter)
            {
                if (histogram != null)
                {
                    foreach (var feature in features)
                    {
                        if (feature is SIFTFeature)
                        {
                            histogram.Add(getter(feature as SIFTFeature));
                        }
                    }
                }
            }
            tally(featuresPerSize, f => f.Size);
            tally(featuresPerResponse, f => f.Response);
            tally(featuresPerOctave, f => f.Octave);
            tally(featuresPerLayer, f => f.Layer);
            tally(featuresPerScale, f => f.Scale);
        }

        public void DumpHistograms(ILogger logger)
        {
            foreach (var h in new Histogram[] { featuresPerImage, featuresPerSize, featuresPerResponse,
                                                featuresPerOctave, featuresPerLayer, featuresPerScale })
            {
                if (h != null)
                {
                    h.Dump(logger);
                }
            }
        }

        //feature detectors only check that the center pixel of the feature is not masked
        //here we check that all pixels in the feature rect are in bounds and valid both in img and mask
        private static IEnumerable<SIFTFeature> FilterInvalid(IEnumerable<SIFTFeature> features, Image img, Image mask)
        {
            foreach (var feat in features)
            {
                int row = (int)feat.Location.Y;
                int col = (int)feat.Location.X;
                int radius = (int)(0.5*feat.Size); //yes, round down
                int minR = row - radius;
                int maxR = row + radius;
                if (minR < 0 || maxR >= img.Height)
                {
                    continue;
                }
                int minC = col - radius;
                int maxC = col + radius;
                if (minC < 0 || maxC >= img.Width)
                {
                    continue;
                }
                bool ok = true;
                for (int r = minR; ok && r <= maxR; r++)
                {
                    for (int c = minC; ok && c <= maxC; c++)
                    {
                        ok &= img.IsValid(r, c) && mask[0, r, c] != 0;
                    }
                }
                if (ok)
                {
                    yield return feat;
                }
            }
        }
    }

    public class FeatureDetecting
    {
        public const int DEF_MASK_BORDER = 10;

        /// <summary>
        /// this is a little confusing because Landform Images can have boolean mask arrays
        /// but the feature detection APIs don't respect those
        /// partly because some of those APIs send images to OpenCV
        /// instead, we need a separate mask binary image which is 0 for masked pixels
        ///
        /// the image mask we use for feature detection purposes combines three things
        /// 1) rover mask
        /// 2) invalid pixels in the original image
        /// 3) inset borders of the original image (image borders sometimes have solid bars)
        /// </summary>
        public static Image MakeMask(PipelineCore pipeline, string roverMaskUrl, Image img, string observationName,
                                     int border = DEF_MASK_BORDER)
        {
            //do not mutate rover mask if it's loaded from mission product (clone: true)
            Image mask = RoverMask.LoadOrBuild(pipeline, roverMaskUrl, img, observationName, clone: true);

            //propagate invalid image pixels to mask
            if (img.Metadata is PDSMetadata)
            {
                var parser = new PDSParser((PDSMetadata)img.Metadata);
                if (parser.HasMissingConstant)
                {
                    float[] missing = parser.MissingConstant.Select(x => (float)x).ToArray();
                    //we could do it this way, but it's just a few more lines to avoid allocating the mask array
                    //mask.UnionMask(img, missing);
                    //mask.SetValuesForMaskedData(new float[] { 0 });
                    for (int row = 0; row < img.Height; row++)
                    {
                        for (int col = 0; col < img.Width; col++)
                        {
                            if (img.BandValuesEqual(row, col, missing))
                            {
                                mask[0, row, col] = 0;
                            }
                        }
                    } 
                }
            }

            //add borders to mask
            border = Math.Min(mask.Height / 2, Math.Min(mask.Width / 2, border));
            for (int b = 0; b < border; b++)
            {
                //whole row
                for(int col = 0; col < mask.Width; col++)
                {
                    mask[0, b, col] = 0;
                    mask[0, mask.Height - 1 - b, col] = 0;
                }

                //whole column
                for (int row = 0; row < mask.Height; row++)
                {
                    mask[0, row, b] = 0;
                    mask[0, row, mask.Width - 1 - b] = 0;
                }
            }
            return mask;
        }

        public static Image DrawFeatures(Image img, Image mask, ImageFeature[] features, string imageName = null)
        {
            var ret = (new Image(img)).ApplyStdDevStretch().ToEmgu<Bgr>();

            //alpha blend mask into green channel
            if (mask != null)
            {
                float alpha = 0.1f;
                for (int row = 0; row < img.Height; row++)
                {
                    for (int col = 0; col < img.Width; col++)
                    {
                        float green = ret.Data[row, col, 1] / 255.0f;
                        green = (1.0f - alpha) * green + alpha * mask[0, row, col];
                        ret.Data[row, col, 1] = (byte)(green * 255);
                    }
                }
            }

            var siftFeat = features.Cast<SIFTFeature>().ToArray();
            var noRange = new VectorOfKeyPoint(siftFeat.Where(f => !(f.Range > 0)).CastToMKeyPoint().ToArray());
            Features2DToolbox.DrawKeypoints(ret, noRange, ret, new Bgr(255, 0, 0), //actually RGB
                                            Features2DToolbox.KeypointDrawType.DrawRichKeypoints);
            var withRange = new VectorOfKeyPoint(siftFeat.Where(f => f.Range > 0).CastToMKeyPoint().ToArray());
            Features2DToolbox.DrawKeypoints(ret, withRange, ret, new Bgr(0, 255, 0), //actually RGB
                                            Features2DToolbox.KeypointDrawType.DrawRichKeypoints);
            if (imageName != null)
            {
                ret.Draw(imageName, new System.Drawing.Point(5, 30),
                         FontFace.HersheySimplex, 1, new Bgr(255, 0, 255), 2);
                if (img.Metadata is PDSMetadata)
                {
                    int sol = (new PDSParser((PDSMetadata)img.Metadata)).PlanetDayNumber;
                    ret.Draw("sol" + sol, new System.Drawing.Point(5, 60),
                             FontFace.HersheySimplex, 1, new Bgr(255, 0, 255), 2);
                }
            }
            return ret.ToOPSImage();
        }

        public static int AddRange(IEnumerable<ImageFeature> features, Image img, Image points)
        {
            //can't check range origin here because img is not actually a range image
            //so it does not  have the necessary PDS header data for that
            var center = Meshing.CheckCameraCenter(img, "AddRange", checkRangeOrigin: false);
            var xyr = Meshing.ConvertPoints(points);
            int n = 0;
            foreach (var feature in features)
            {
                int row = (int)feature.Location.Y;
                int col = (int)feature.Location.X;
                int radius = (int)(0.5*((SIFTFeature)feature).Size); //yes, round down
                int minR = Math.Max(0, row - radius);
                int maxR = Math.Min(img.Height - 1, row + radius);
                int minC = Math.Max(0, col - radius);
                int maxC = Math.Min(img.Width - 1, col + radius);
                var avg = new Vector3();
                double valid = 0;
                for (int r = minR; r <= maxR; r++)
                {
                    for (int c = minC; c <= maxC; c++)
                    {
                        if (!xyr.IsInvalid(r, c))
                        {
                            avg += new Vector3(xyr[0, r, c], xyr[1, r, c], xyr[2, r, c]);
                            valid++;
                        }
                    }
                }
                if (valid > 0)
                {
                    feature.Range = Vector3.Distance(avg / valid, center);
                    n++;
                }
            }
            return n;
        }
    }
}
