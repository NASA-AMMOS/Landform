using log4net;
using OPS.Geometry;
using OPS.Plumbing;
using OPS.Pipeline.TileServer;
using System.Collections.Generic;
using System.Linq;

namespace OPS.Pipeline.TileServer
{
    abstract class PipelineStateMachine
    {
        protected static ILog logger = LogManager.GetLogger(typeof(PipelineStateMachine));

        protected PipelineCore pipeline;
        protected TilingQueue workerQueue;
        protected ProjectCache projectCache;

        public PipelineStateMachine(PipelineCore pipeline, TilingQueue workerQueue, string projectName)
        {
            this.pipeline = pipeline;
            this.workerQueue = workerQueue;
            this.projectCache = new ProjectCache(pipeline, projectName);
        }
       
        abstract public void ProcessMessage(TilingQueueMessage m);
    }

    // *** common messages for all existing pipelines *** //

    public class TileCompletedMessage : TilingQueueMessage
    {
        public string TileId;

        public TileCompletedMessage(string projectName, string id) : base(projectName)
        {
            this.TileId = id;
        }
    }


}