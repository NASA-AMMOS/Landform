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
    public enum DetectorType
    {
        SIFT,
        ASIFT,
        PCASIFT
    }

    public class FeatureDetector
    {
        public readonly DetectorType Detector;

        private readonly double minFeatureSize;
        private readonly int maxFeatures;
        private readonly PipelineCore pipeline;

        public const int DEF_MAX_FEATURES = 10000;
        public const double DEF_MIN_FEATURE_SIZE = 0;

        public FeatureDetector(PipelineCore pipeline, DetectorType detector, int maxFeatures = DEF_MAX_FEATURES,
                               double minFeatureSize = DEF_MIN_FEATURE_SIZE)
        {
            this.pipeline = pipeline;
            this.Detector = detector;
            this.maxFeatures = maxFeatures;
            this.minFeatureSize = minFeatureSize;
        }

        private PCAKeypointProjector projector;
        public ImageFeature[] DetectPCASIFT(Image img, Image mask)
        {
            if (projector == null)
            {
                string gpcafile = PCAKeypointProjector.DefaultTrainingSpace;
                projector = new PCAKeypointProjector(gpcafile, false);
            }
            List<PCASIFTFeature> features = new PCASIFTDetector().Detect(img, mask).Cast<PCASIFTFeature>().ToList();
            projector.Project(img, features, 1);
            return features.ToArray();
        }

        public ImageFeature[] Detect(Image img, Image mask)
        {
            if (Detector != DetectorType.ASIFT)
            {
                //https://github.jpl.nasa.gov/OnSight/Landform/issues/435
                pipeline.LogWarn("{0} feature detector may not be maintained", Detector);
            }

            ImageFeature[] features = null;
            switch (Detector)
            {
                case DetectorType.PCASIFT: features = DetectPCASIFT(img, mask); break;
                case DetectorType.ASIFT: features = (new ASIFTDetector()).Detect(img, mask).ToArray(); break;
                case DetectorType.SIFT: features = (new SIFT()).Detect(img, mask).ToArray(); break;
                default: throw new NotImplementedException("unhandled feature detector " + Detector);
            }
            return features
                .Where(f => ((SIFTFeature)f).Size >= minFeatureSize)
                .OrderByDescending(f => ((SIFTFeature)f).Response)
                .Take(maxFeatures)
                .ToArray();
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
                pipeline.LogError("failed to detect {0} features for {1}", Detector, observationName, ex);
                return null;
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

        public static Image CompositeMask(Image img, Image mask, float alpha = 0.1f)
        {
            Image ret = new Image(3, img.Width, img.Height);
            for (int row = 0; row < img.Height; row++)
            {
                for (int col = 0; col < img.Width; col++)
                {
                    float gray = 0;
                    for (int band = 0; band < img.Bands; band++)
                    {
                        gray += img[0, row, col];
                    }
                    gray /= img.Bands;
                    ret[0, row, col] = gray;
                    ret[1, row, col] = (1.0f - alpha) * gray + alpha * mask[0, row, col];
                    ret[2, row, col] = gray;
                }
            }
            return ret;
        }

        public static Image DrawFeatures(Image img, Image mask, ImageFeature[] features, string imageName = null)
        {
            var ret = CompositeMask(img, mask).ToEmgu<Bgr>();
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
            var c = Meshing.CheckCameraCenter(img, "AddRange", checkRangeOrigin: false);
            var xyr = Meshing.ConvertPoints(points);
            int n = 0;
            foreach (var feature in features)
            {
                int row = (int)feature.Location.Y;
                int col = (int)feature.Location.X;
                if (!xyr.IsInvalid(row, col))
                {
                    var p = new Vector3(xyr[0, row, col], xyr[1, row, col], xyr[2, row, col]);
                    feature.Range = Vector3.Distance(p, c);
                    n++;
                }
            }
            return n;
        }
    }
}
