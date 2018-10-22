using System;
using System.Linq;
using System.Collections.Generic;
using OPS.Util;
using OPS.Plumbing;
using OPS.Geometry;
using OPS.Pipeline.MeshWorker;
using log4net;

namespace OPS.Pipeline.TileServer
{
    class MSLStateMachine : PipelineStateMachine
    {
        public MSLStateMachine(PipelineCore pipeline, TilingQueue workerQueue, string projectName)
            : base(pipeline, workerQueue, projectName)
        {
        }

        override protected TypeDispatcher InitDispatcher()
        {
            return base.InitDispatcher()
                .Case((BuildTilingInputMessage m) => {
                        LogInfo("tiling input built, defining tiles");
                        workerQueue.Enqueue(new DefineTilesMessage(projectName));
                    });
        }

        override protected void RunProject()
        {
            LogInfo("building tiling input");
            RunProject(new BuildTilingInputMessage(projectName));
        }

        protected override TilingQueueMessage MakeLeafJobMessage(List<string> leaves)
        {
            return new BuildBackprojectLeavesMessage(projectName, leaves);
        }
    }
}
