using System;
using System.Collections.Generic;
using JPLOPS.ImageFeatures;

namespace JPLOPS.Pipeline.AlignmentServer
{
    public class FeatureMatches
    {
        [DBRangeKey]
        public string ProjectName;

        [DBHashKey]
        public string Name;

        public string ModelName;

        public string DataName;

        public Guid MatchesGuid;

        protected void IsValid()
        {
            if (!(ProjectName != null && Name != null &&
                  MatchesGuid != null && MatchesGuid != Guid.Empty))
            {
                throw new Exception("missing required property in FeatureMatches");
            }
        }

        public FeatureMatches() { }

        protected FeatureMatches(string projectName, string name, string modelName, string dataName, Guid matchesGuid)
            
        {
            this.ProjectName = projectName;
            this.Name = name;
            this.ModelName = modelName;
            this.DataName = dataName;
            this.MatchesGuid = matchesGuid;
            IsValid();
        }

        public static FeatureMatches Create(PipelineCore pipeline, Project project, string name,
                                            string modelName, string dataName, FeatureMatch[] matches)
        {
            var matchesProd = new FeatureMatchesDataProduct(matches);
            pipeline.SaveDataProduct(project, matchesProd, noCache: true);
            var ret = new FeatureMatches(project.Name, name, modelName, dataName, matchesProd.Guid);
            ret.Save(pipeline);
            return ret;
        }

        public static FeatureMatches Create(PipelineCore pipeline, Project project,
                                            string modelName, string dataName, FeatureMatch[] matches)
        {
            return Create(pipeline, project, modelName + "-" + dataName, modelName, dataName, matches);
        }

        public virtual void Save(PipelineCore pipeline)
        {
            IsValid();
            pipeline.SaveDatabaseItem(this);
        }

        public static FeatureMatches Find(PipelineCore pipeline, string projectName, string name)
        {
            return pipeline.LoadDatabaseItem<FeatureMatches>(name, projectName);
        }

        public static IEnumerable<FeatureMatches> Find(PipelineCore pipeline, string projectName)
        {
            return pipeline.ScanDatabase<FeatureMatches>("ProjectName", projectName);
        }
    }
}
