using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Amazon.DynamoDBv2.DataModel;
using OPS.Cloud;
using OPS.Alignment;
using OPS.Pipeline;

namespace OPS.Pipeline.AlignmentServer
{
    [DynamoDBTable("FeatureMatches")]
    [DynamoDBReadCapacity(50, 100)]
    [DynamoDBWriteCapacity(50, 100)]
    public class FeatureMatches
    {
        [DynamoDBRangeKey]
        public string ProjectName;

        [DynamoDBHashKey]
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

        //This constructor must be public for DynamoDb but should not be used
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
            pipeline.SaveDataProduct(project, matchesProd);
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
