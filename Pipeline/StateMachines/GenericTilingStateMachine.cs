using System;
using System.Linq;
using System.Collections.Generic;
using OPS.Plumbing;
using OPS.Geometry;
using log4net;

namespace OPS.Pipeline.TileServer
{
    class GenericTilingStateMachine : PipelineStateMachine
    {
        protected static ILog logger = LogManager.GetLogger(typeof(GenericTilingStateMachine));

        public GenericTilingStateMachine(PipelineCore pipeline, TilingQueue workerQueue, string projectName)
            : base(pipeline, workerQueue, projectName)
        {
        }

        static public string ProjectType()
        {
            return "GenericTiling";
        }

        protected override bool SkipChunking(TilingProject project)
        {
            return project.TilingScheme == TilingScheme.UserDefined.ToString();
        }

        protected override TilingQueueMessage MakeLeafJobMessage(List<string> leaves)
        {
            return new BuildBakedLeavesMessage(projectName, leaves);
        }
    }
}
