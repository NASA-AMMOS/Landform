using log4net;
using OPS.Plumbing;

namespace OPS.Pipeline.TileServer
{

    public class TileServerOperation
    {
        protected string projectName;
        protected PipelineCore pipeline;
        protected TileServerCloud cloud;
        private ILog logger;

        public TileServerOperation(string projectName, PipelineCore pipeline, TileServerCloud cloud, ILog logger)
        {
            this.projectName = projectName;
            this.pipeline = pipeline;
            this.cloud = cloud;
            this.logger = logger;
        }

        protected void LogInfo(string msg)
        {
            logger.Info(string.Format("[{0}] {1}", projectName, msg));
        }

        protected void LogError(string msg)
        {
            logger.Error(string.Format("[{0}] {1}", projectName, msg));
        }

        protected void LogWarn(string msg)
        {
            logger.Warn(string.Format("[{0}] {1}", projectName, msg));
        }
    }
}
