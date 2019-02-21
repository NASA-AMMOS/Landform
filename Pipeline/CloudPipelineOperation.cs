using log4net;
using System;
using OPS.Cloud;

namespace OPS.Pipeline
{
    public class CloudPipelineOperation
    {
        protected readonly CloudPipeline pipeline;
        protected readonly string projectName;

        //intentionally not adding "message" field here so that subclasses can add their own type-specific one

        public CloudPipelineOperation(CloudPipeline pipeline, QueueMessage msg)
        {
            this.pipeline = pipeline;
            this.projectName = msg.ProjectName;
            pipeline.LogPrefix = string.Format("[{0}] {1} {2}: ", msg.ProjectName, GetType().Name, msg.MessageId);
        }
    }
}
