using log4net;
using System;

//TODO: refactor so that local codepath does not have cloud dependencies
//https://github.jpl.nasa.gov/OnSight/Landform/issues/596
using QueueMessage = OPS.Cloud.QueueMessage;

namespace OPS.Pipeline
{
    public class PipelineOperation
    {
        protected readonly PipelineCore pipeline;
        protected readonly string projectName;

        //intentionally not adding "message" field here so that subclasses can add their own type-specific one

        public PipelineOperation(PipelineCore pipeline, QueueMessage msg)
        {
            this.pipeline = pipeline;
            this.projectName = msg.ProjectName;
            pipeline.LogPrefix = string.Format("[{0}] {1} {2}: ", msg.ProjectName, GetType().Name, msg.MessageId);
        }
    }
}
