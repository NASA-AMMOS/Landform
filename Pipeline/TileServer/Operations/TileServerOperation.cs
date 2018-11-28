using log4net;
using OPS.Plumbing;
using System;

namespace OPS.Pipeline.TileServer
{

    public class TileServerOperation
    {
        protected string projectName;
        protected string messageId;
        protected PipelineCore pipeline;
        protected TileServerCloud cloud;

        public TileServerOperation(TilingQueueMessage message, PipelineCore pipeline, TileServerCloud cloud)
        {
            this.projectName = message.ProjectName;
            this.messageId = message.MessageId;
            this.pipeline = pipeline;
            this.cloud = cloud;
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
