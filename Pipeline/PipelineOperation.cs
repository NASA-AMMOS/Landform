using log4net;
using System;

namespace OPS.Pipeline
{
    public class PipelineOperation
    {
        public static bool LessSpew;

        protected readonly PipelineCore pipeline;
        protected readonly string projectName;
        protected readonly string messageId;
        protected readonly string logPrefix;

        //intentionally not adding "message" field here so that subclasses can add their own type-specific one

        public PipelineOperation(PipelineCore pipeline, PipelineMessage msg)
        {
            this.pipeline = pipeline;
            this.projectName = msg.ProjectName;
            this.messageId = msg.MessageId;
            this.logPrefix = string.Format("[{0}] {1}{2}", projectName, GetType().Name,
                                           !string.IsNullOrEmpty(messageId) ? (" " + messageId) : "");
        }

        protected void LogInfo(string msg, params Object[] args)
        {
            msg = string.Format(msg, args);
            if (LessSpew)
            {
                pipeline.LogVerbose("{0} {1}", logPrefix, msg);
            }
            else
            {
                pipeline.LogInfo("{0} {1}", logPrefix, msg);
            }
            SendStatusToMaster(msg);
        }

        protected void LogVerbose(string msg, params Object[] args)
        {
            pipeline.LogVerbose("{0} {1}", logPrefix, string.Format(msg, args));
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
            msg = string.Format(msg, args);
            pipeline.LogError("{0} {1}", logPrefix, msg);
            SendStatusToMaster("error: " + msg);
        }

        protected void SendStatusToMaster(string status, bool done = false)
        {
            pipeline.EnqueueToMaster(new StatusMessage(projectName, messageId, GetType().Name, status, done));
        }
    }
}
