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
using OPS.Pipeline.AlignmentServer;

namespace OPS.Pipeline
{
    public class FeatureDetector
    {
        public enum DetectorType
        {
            SIFT,
            ASIFT,
            PCASIFT,
            FAST
        }

        public const DetectorType DEF_DETECTOR_TYPE = DetectorType.ASIFT;
        public const double DEF_MIN_FEATURE_SIZE = 0;
        public const double DEF_MIN_RESPONSE = -1;
        public const double DEF_MAX_RESPONSE = -1;
        public const int DEF_EXTRA_INVALID_RADIUS = 0;
        public const int DEF_MAX_FEATURES = 10000;
        public const int DEF_DECIMATION = 1;
        public const int DEF_SIFT_OCTAVES = 4;
        public const int DEF_MIN_SIFT_OCTAVE = -1;
        public const int DEF_MAX_SIFT_OCTAVE = -1;
        public const int DEF_FAST_THRESHOLD = 10;

        public class Options
        {
            public DetectorType DetectorType = DEF_DETECTOR_TYPE;
            public double MinFeatureSize = DEF_MIN_FEATURE_SIZE;
            public double MinResponse = DEF_MIN_RESPONSE;
            public double MaxResponse = DEF_MAX_RESPONSE;
            public int ExtraInvalidRadius = DEF_EXTRA_INVALID_RADIUS;
            public int MaxFeatures = DEF_MAX_FEATURES;
            public int Decimation = DEF_DECIMATION;
            public int SIFTOctaves = DEF_SIFT_OCTAVES;
            public int MinSIFTOctave = DEF_MIN_SIFT_OCTAVE;
            public int MaxSIFTOctave = DEF_MAX_SIFT_OCTAVE;
            public int FASTThreshold = DEF_FAST_THRESHOLD;
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
        private readonly RoverMasker masker;
        private readonly Options options;

        public FeatureDetector(PipelineCore pipeline, RoverMasker masker, Options options = null)
        {
            this.pipeline = pipeline;
            this.masker = masker;
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

        public delegate double FeatureSortKey(SIFTFeature feature);

        public ImageFeature[] Detect(Image img, Image mask, FeatureSortKey sortKey = null)
        {
            if (options.Decimation > 1)
            {
                img = img.Decimated(options.Decimation);
                mask = mask.Decimated(options.Decimation);
            }

            Func<SIFTFeature, bool> filter = f =>
                (f.Size * options.Decimation >= options.MinFeatureSize) &&
                (options.MinResponse < 0 || f.Response >= options.MinResponse) &&
                (options.MaxResponse < 0 || f.Response <= options.MaxResponse) &&
                (options.MinSIFTOctave < 0 || f.Octave >= options.MinSIFTOctave) &&
                (options.MaxSIFTOctave < 0 || f.Octave <= options.MaxSIFTOctave);

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
                    detector = new ASIFTDetector() { OctaveLayers = options.SIFTOctaves, Filter = filter };
                    break;
                }
                case DetectorType.PCASIFT:
                {
                    detector = new PCASIFTDetector() { OctaveLayers = options.SIFTOctaves };
                    break;
                }
                case DetectorType.FAST:
                {
                    detector = new FASTDetector() { Threshold = options.FASTThreshold };
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

            if (sortKey == null)
            {
                sortKey = (SIFTFeature f) => -f.Response;
            }

            features = features
                .Where(filter)
                .OrderBy(f => sortKey(f))
                .Take(options.MaxFeatures)
                .ToArray();

            //add descriptors now that we've culled down the features and eliminated bad ones
            //this can save quite a bit of time
            //but also we have seen crashes in the emgucv code to collect SIFT feature descriptors
            //and computing them here hopefully limits the impact of that
            //some detectors will have already added descriptors
            //e.g. ASIFT does that because the descriptors are based on temporary warped copies of the image
            var featuresWithoutDescriptors = features.Where(f => f.Descriptor == null).ToArray();
            if (featuresWithoutDescriptors.Length > 0)
            {
                detector.AddDescriptors(img, featuresWithoutDescriptors);
            }

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

        public DetectedFeatures Detect(string imageUrl, string roverMaskUrl, Project project)
        {
            var observationName = StringHelper.GetLastUrlPathSegment(imageUrl, stripExtension: true);
            try
            {
                var obs = Observation.Find(pipeline, project.Name, observationName);
                var img = pipeline.LoadImage(imageUrl);
                Image mask = ImageMasker.GetOrCreateMask(pipeline, project, obs, masker, roverMaskUrl, img);
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
        private IEnumerable<SIFTFeature> FilterInvalid(IEnumerable<SIFTFeature> features, Image img, Image mask)
        {
            foreach (var feat in features)
            {
                int row = (int)feat.Location.Y;
                int col = (int)feat.Location.X;
                int radius = (int)(0.5*feat.Size); //yes, round down
                radius += options.ExtraInvalidRadius;
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
        public static Image DrawFeatures(Image img, Image mask, ImageFeature[] features, string imageName = null,
                                         bool stretch = true)
        {
            return DrawFeaturesEmgu(img, mask, features, imageName, stretch).ToOPSImage();
        }

        public static Image<Bgr, byte> DrawFeaturesEmgu(Image img, Image mask, ImageFeature[] features,
                                                        string imageName = null, bool stretch = true)
        {
            var ret = stretch ?  (new Image(img)).ApplyStdDevStretch().ToEmgu<Bgr>() : img.ToEmgu<Bgr>();

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
            return ret;
        }

        /// <summary>
        /// NOTE: it is subtly incorrect to use a range map to substitute for an XYZ map
        /// because stereo correlation often uses 2D disparity
        /// which means the recovered surface point for a pixel
        /// may not actually lie on the ray through that pixel
        /// but for some missions (MSL) we only have range products
        /// https://github.jpl.nasa.gov/OnSight/Landform/issues/471
        /// </summary>
        public static int AddRange(IEnumerable<ImageFeature> features, Image xyzOrRng)
        {
            PDSParser parser = new PDSParser((PDSMetadata)xyzOrRng.Metadata);
            float missingConstant = float.NaN;
            bool hasMissingConstant = false;
            Image rng = null, xyr = null;
            var center = PDSImage.CheckCameraCenter(parser, xyzOrRng, "AddRange");
            switch (parser.DerivedImageType)
            {
                case RoverProductType.Range:
                {
                    if (parser.HasMissingConstant)
                    {
                        //raw range image may not have mask set from missing constant
                        missingConstant = (float)parser.MissingConstant[0];
                        hasMissingConstant = true;
                    }
                    rng = xyzOrRng;
                    break;
                }
                case RoverProductType.Points:
                {
                    //PDSImage.ConvertPoints() will set mask from missing constant
                    xyr = (new PDSImage(xyzOrRng, parser)).ConvertPoints();
                    if (xyr == null)
                    {
                        return 0;
                    }
                    break;
                }
                default: throw new ArgumentException("unsupported range image type: " + parser.DerivedImageType);
            }

            int n = 0;
            foreach (var feature in features)
            {
                int row = (int)feature.Location.Y;
                int col = (int)feature.Location.X;
                if (row >= xyzOrRng.Height || col >= xyzOrRng.Width)
                {
                    throw new ArgumentException(string.Format("feature at ({0}, {1}) outside {2}x{3} range image",
                                                              col, row, xyzOrRng.Width, xyzOrRng.Height));
                }
                int radius = (int)(0.5*((SIFTFeature)feature).Size); //yes, round down
                int minR = Math.Max(0, row - radius);
                int maxR = Math.Min(xyzOrRng.Height - 1, row + radius);
                int minC = Math.Max(0, col - radius);
                int maxC = Math.Min(xyzOrRng.Width - 1, col + radius);
                float sum = 0;
                int valid = 0;
                for (int r = minR; r <= maxR; r++)
                {
                    for (int c = minC; c <= maxC; c++)
                    {
                        if (xyzOrRng.IsValid(r, c))
                        {
                            if (rng != null)
                            {
                                float d = rng[0, r, c];
                                if (!hasMissingConstant || d != missingConstant)
                                {
                                    sum += d;
                                    valid++;
                                }
                            }
                            else
                            {
                                sum += (float) Vector3.Distance(new Vector3(xyr[0, r, c], xyr[1, r, c], xyr[2, r, c]),
                                                                center);
                                valid++;
                            }
                        }
                    }
                }
                if (valid > 0)
                {
                    feature.Range = sum / valid;
                    n++;
                }
            }
            return n;
        }
    }
}
