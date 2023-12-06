using System;
using JPLOPS.Util;

namespace JPLOPS.Pipeline
{
    public class PipelineOperation : ILogger
    {
        public static bool LessSpew;
        public static bool SingleWorkflowSpew;

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

            logPrefix = "";
            if (!SingleWorkflowSpew)
            {
                logPrefix = string.Format("[{0}] {1} ", projectName, GetType().Name);
            }
            if (!string.IsNullOrEmpty(messageId))
            {
                logPrefix += messageId + " ";
            }
        }

        public void LogLess(string msg, params Object[] args)
        {
            msg = string.Format(msg, args);
            if (LessSpew)
            {
                pipeline.LogVerbose(logPrefix + msg);
            }
            else
            {
                pipeline.LogInfo(logPrefix + msg);
            }
            SendStatusToMaster(msg);
        }

        public void LogInfo(string msg, params Object[] args)
        {
            pipeline.LogInfo(logPrefix + string.Format(msg, args));
        }

        public void LogVerbose(string msg, params Object[] args)
        {
            pipeline.LogVerbose(logPrefix + string.Format(msg, args));
        }

        public void LogDebug(string msg, params Object[] args)
        {
            pipeline.LogDebug(logPrefix + string.Format(msg, args));
        }

        public void LogWarn(string msg, params Object[] args)
        {
            pipeline.LogWarn(logPrefix + string.Format(msg, args));
        }

        public void LogError(string msg, params Object[] args)
        {
            msg = string.Format(msg, args);
            pipeline.LogError(logPrefix + msg);
            SendStatusToMaster("error: " + msg); //don't pass error=true, will cause state machine to abort 
        }

        public void LogException(Exception ex, string msg = null, int maxAggregateSpew = 1, bool stackTrace = false)
        {
            msg = logPrefix + (msg ?? "");
            pipeline.LogException(ex, msg, maxAggregateSpew, stackTrace);
            msg = string.Format("{0}{1}", !string.IsNullOrEmpty(msg) ? (msg + ": ") : "", ex.Message);
            SendStatusToMaster("error: " + msg); //don't pass error=true, will cause state machine to abort 
        }

        protected void SendStatusToMaster(string status, bool done = false, bool error = false)
        {
            pipeline.EnqueueToMaster(new StatusMessage(projectName, messageId, GetType().Name, status, done, error));
        }
    }
}
