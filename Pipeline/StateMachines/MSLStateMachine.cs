using System;
using System.Linq;
using System.Collections.Generic;
using OPS.Plumbing;
using OPS.Geometry;
using OPS.Pipeline.MeshWorker;
using log4net;

namespace OPS.Pipeline.TileServer
{
    class MSLStateMachine : PipelineStateMachine
    {
        private static ILog logger = LogManager.GetLogger(typeof(MSLStateMachine));

        public MSLStateMachine(PipelineCore pipeline, TilingQueue workerQueue, string projectName)
            : base(pipeline, workerQueue, projectName)
        {
        }

        static public string ProjectType()
        {
            return "MSL";
        }

        public override bool ProcessMessage(TilingQueueMessage m)
        {
            if (base.ProcessMessage(m))
            {
                return true;
            }
            if (m.GetType() == typeof(BuildTilingInputMessage))
            {
                logger.Info("tiling input built in project " + projectName);
                workerQueue.Enqueue(new DefineTilesMessage(projectName));
                return true;
            }
            return false;
        }

        override protected void RunProject()
        {
            RunProject(new BuildTilingInputMessage(projectName));
        }

        protected override TilingQueueMessage MakeLeafJobMessage(List<string> leaves)
        {
            return new BuildBackprojectLeavesMessage(projectName, leaves);
        }
    }
}
