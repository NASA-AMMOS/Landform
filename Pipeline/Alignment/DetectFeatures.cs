using OPS.Alignment;
using OPS.Plumbing;
using System;
using System.Collections.Generic;
using System.Linq;
using Amazon.EC2.Model;
using System.Text;
using System.Threading.Tasks;
using OPS.Cloud;

namespace OPS.Pipeline
{
    public class DetectFeatures : PipelineRoutine
    {
        public enum FeatureDetector
        {
            SIFT,
            ASIFT,
            PCASIFT
        }
        public FeatureDetector Detector;
        public DetectFeatures(PipelineCore pipeline, FeatureDetector detector) : base(pipeline)
        {
            Detector = detector;
            projector = null;
        }

        PCAKeypointProjector projector;

        public ImageFeature[] DetectPCASIFT(Imaging.Image img, Imaging.Image mask)
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
        public ImageFeature[] DetectWith(IFeatureDetector det, Imaging.Image img, Imaging.Image mask)
        {
            return det.Detect(img, mask).ToArray();
        }

        public DetectedFeatures Detect(Observation obs)
        {
            ImageFeature[] features;

            Imaging.Image img = GetImage(new ObservationImageRef(obs));
            Imaging.Image mask = null; //(obs.MaskGuid != Guid.Empty) ? Pipeline.Get<ImageDataProduct>(obs.ProjectName, obs.MaskGuid).Image : null;

            if (Detector == FeatureDetector.PCASIFT)
            {
                features = DetectPCASIFT(img, mask);
            }
            else if (Detector == FeatureDetector.ASIFT)
            {
                features = DetectWith(new ASIFTDetector(), img, mask);
            }
            else if (Detector == FeatureDetector.SIFT)
            {
                features = DetectWith(new SIFT(), img, mask);
            }
            else
            {
                throw new NotImplementedException();
            }
            
            DetectedFeatures detected = new DetectedFeatures();
            detected.Features = features;
            detected.ObservationName = obs.Name;
            return detected;
        }
    }
}
