using System;
using System.Collections.Generic;

namespace JPLOPS.Pipeline.AlignmentServer
{
    public class SpatialMatches
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
                throw new Exception("missing required property in SpatialMatches");
            }
        }

        public SpatialMatches() { }

        protected SpatialMatches(string projectName, string name, string modelName, string dataName, Guid matchesGuid)
            
        {
            this.ProjectName = projectName;
            this.Name = name;
            this.ModelName = modelName;
            this.DataName = dataName;
            this.MatchesGuid = matchesGuid;
            IsValid();
        }

        public static SpatialMatches Create(PipelineCore pipeline, Project project, string name,
                                            string modelName, string dataName, SpatialMatch[] matches)
        {
            var matchesProd = new SpatialMatchesDataProduct(matches);
            pipeline.SaveDataProduct(project, matchesProd, noCache: true);
            var ret = new SpatialMatches(project.Name, name, modelName, dataName, matchesProd.Guid);
            ret.Save(pipeline);
            return ret;
        }

        public static SpatialMatches Create(PipelineCore pipeline, Project project,
                                            string modelName, string dataName, SpatialMatch[] matches)
        {
            return Create(pipeline, project, modelName + "-" + dataName, modelName, dataName, matches);
        }

        public virtual void Save(PipelineCore pipeline)
        {
            IsValid();
            pipeline.SaveDatabaseItem(this);
        }

        public static SpatialMatches Find(PipelineCore pipeline, string projectName, string name)
        {
            return pipeline.LoadDatabaseItem<SpatialMatches>(name, projectName);
        }

        public static IEnumerable<SpatialMatches> Find(PipelineCore pipeline, string projectName)
        {
            return pipeline.ScanDatabase<SpatialMatches>("ProjectName", projectName);
        }
    }
}
