using System;
using System.Linq;
using System.Collections.Generic;
using log4net;
using OPS.Util;
using OPS.Cloud;
using OPS.Pipeline.MeshWorker;
using OPS.Pipeline.TileServer;

namespace OPS.Pipeline
{
    class MSLStateMachine : PipelineStateMachine
    {
        public MSLStateMachine(CloudPipeline pipeline, string projectName) : base(pipeline, projectName)
        {
        }

        override protected TypeDispatcher InitDispatcher()
        {
            return base.InitDispatcher()
                .Case((BuildTilingInputMessage m) => {
                        LogInfo("tiling input built, defining tiles");
                        pipeline.WorkerQueue.Enqueue(new DefineTilesMessage(projectName));
                    });
        }

        override protected void RunProject()
        {
            LogInfo("building tiling input");
            RunProject(new BuildTilingInputMessage(projectName));
        }

        protected override QueueMessage MakeLeafJobMessage(List<string> leaves)
        {
            return new BuildBackprojectLeavesMessage(projectName) { TileIds = leaves};
        }
    }
}
