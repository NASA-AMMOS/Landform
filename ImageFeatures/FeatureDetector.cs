using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using JPLOPS.Util;
using JPLOPS.Imaging;

namespace JPLOPS.ImageFeatures
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

        private readonly ILogger logger;
        private readonly Options options;

        public FeatureDetector(Options options = null, ILogger logger = null)
        {
            this.options = options ?? new Options();
            this.logger = logger;

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

            FeatureDetectorBase detector = null;
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

            logger.LogDebug("min size {0}, max size {1}",
                            features.Select(f => f.Size * options.Decimation).Min(),
                            features.Select(f => f.Size * options.Decimation).Max());
            logger.LogDebug("min response {0}, max response {1}",
                            features.Select(f => f.Response).Min(), features.Select(f => f.Response).Max());
            logger.LogDebug("min octave {0}, max octave {1}",
                            features.Select(f => f.Octave).Min(), features.Select(f => f.Octave).Max());

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
}
