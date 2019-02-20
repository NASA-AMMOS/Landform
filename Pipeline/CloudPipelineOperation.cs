using log4net;
using System;
using OPS.Cloud;

namespace OPS.Pipeline
{
    public class CloudPipelineOperation
    {
        protected readonly CloudPipeline pipeline;
        protected readonly string projectName;
        protected readonly string messageId;

        //intentionally not adding "message" field here so that subclasses can add their own type-specific one

        public CloudPipelineOperation(CloudPipeline pipeline, QueueMessage message)
        {
            this.pipeline = pipeline;
            this.projectName = message.ProjectName;
            this.messageId = message.MessageId;
        }

        protected void LogDebug(string msg, params Object[] args)
        {
            pipeline.LogDebug("[{0}] {1} {2}: {3}", projectName, GetType().Name, messageId, string.Format(msg, args));
        }

        protected void LogInfo(string msg, params Object[] args)
        {
            pipeline.LogInfo("[{0}] {1} {2}: {3}", projectName, GetType().Name, messageId, string.Format(msg, args));
        }

        protected void LogWarn(string msg, params Object[] args)
        {
            pipeline.LogWarn("[{0}] {1} {2}: {3}", projectName, GetType().Name, messageId, string.Format(msg, args));
        }

        protected void LogError(string msg, params Object[] args)
        {
            pipeline.LogError("[{0}] {1} {2}: {3}", projectName, GetType().Name, messageId, string.Format(msg, args));
        }
    }
}
