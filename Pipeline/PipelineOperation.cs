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
        protected readonly string messageId;
        protected readonly string logPrefix;

        //intentionally not adding "message" field here so that subclasses can add their own type-specific one

        public PipelineOperation(PipelineCore pipeline, QueueMessage msg)
        {
            this.pipeline = pipeline;
            this.projectName = msg.ProjectName;
            this.messageId = msg.MessageId;
            this.logPrefix = string.Format("[{0}] {1} {2}", projectName, GetType().Name, messageId);
        }

        protected void LogInfo(string msg, params Object[] args)
        {
            pipeline.LogInfo("{0} {1}", logPrefix, string.Format(msg, args));
        }

        protected void LogDebug(string msg, params Object[] args)
        {
            pipeline.LogDebug("{0} {1}", logPrefix, string.Format(msg, args));
        }

        protected void LogWarn(string msg, params Object[] args)
        {
            pipeline.LogWarn("{0} {1}", logPrefix, string.Format(msg, args));
        }

        protected void LogError(string msg, params Object[] args)
        {
            pipeline.LogError("{0} {1}", logPrefix, string.Format(msg, args));
        }
    }
}
