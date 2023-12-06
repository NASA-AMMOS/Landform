using System;
using System.Collections.Generic;
using JPLOPS.ImageFeatures;

namespace JPLOPS.Pipeline.AlignmentServer
{
    public class BirdsEyeViewFeatures
    {
        [DBRangeKey]
        public string ProjectName;

        [DBHashKey]
        public string Name;

        public FeatureDetector.DetectorType DetectorType;

        public double MinFeatureResponse;

        public int MaxFeatures;

        public int ExtraInvalidRadius;

        public int FASTThreshold;

        public Guid FeaturesGuid;

        protected void IsValid()
        {
            if (!(ProjectName != null && Name != null &&
                  FeaturesGuid != null && FeaturesGuid != Guid.Empty))
            {
                throw new Exception("missing required property in BirdsEyeViewFeatures");
            }
            if (MinFeatureResponse < 0 || MaxFeatures <= 0 || ExtraInvalidRadius < 0 || FASTThreshold < 0)
            {
                throw new Exception("invalid property in BirdsEyeViewFeatures");
            }
        }

        public BirdsEyeViewFeatures() { }

        protected BirdsEyeViewFeatures(string projectName, string name, Guid featuresGuid,
                                       FeatureDetector.DetectorType detectorType, double minFeatureResponse,
                                       int maxFeatures, int extraInvalidRadius, int fastThreshold)
            
        {
            this.ProjectName = projectName;
            this.Name = name;
            this.DetectorType = detectorType;
            this.MinFeatureResponse = minFeatureResponse;
            this.MaxFeatures = maxFeatures;
            this.ExtraInvalidRadius = extraInvalidRadius;
            this.FASTThreshold = fastThreshold;
            this.FeaturesGuid = featuresGuid;
            IsValid();
        }

        public static BirdsEyeViewFeatures Create(PipelineCore pipeline, Project project, string name,
                                                  ImageFeature[] features, FeatureDetector.DetectorType detectorType,
                                                  double minFeatureResponse, int maxFeatures, int extraInvalidRadius,
                                                  int fastThreshold)
        {
            var featProd = new FeaturesDataProduct(features);
            pipeline.SaveDataProduct(project, featProd, noCache: true);
            var ret = new BirdsEyeViewFeatures(project.Name, name, featProd.Guid, detectorType, minFeatureResponse,
                                               maxFeatures, extraInvalidRadius, fastThreshold);
            ret.Save(pipeline);
            return ret;
        }

        public virtual void Save(PipelineCore pipeline)
        {
            IsValid();
            pipeline.SaveDatabaseItem(this);
        }

        public static BirdsEyeViewFeatures Find(PipelineCore pipeline, string projectName, string name)
        {
            return pipeline.LoadDatabaseItem<BirdsEyeViewFeatures>(name, projectName);
        }

        public static IEnumerable<BirdsEyeViewFeatures> Find(PipelineCore pipeline, string projectName)
        {
            return pipeline.ScanDatabase<BirdsEyeViewFeatures>("ProjectName", projectName);
        }
    }
}
