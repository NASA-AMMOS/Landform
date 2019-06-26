using System;
using System.Linq;
using System.Collections.Generic;
using log4net;
using OPS.Cloud;
using OPS.Pipeline.TileServer;

namespace OPS.Pipeline
{
    class GenericTilingStateMachine : PipelineStateMachine
    {
        public GenericTilingStateMachine(PipelineCore pipeline, string projectName) : base(pipeline, projectName)
        {
        }

        protected override bool SkipChunking(TilingProject project)
        {
            return project.TilingScheme == TilingScheme.UserDefined.ToString();
        }

        protected override QueueMessage MakeLeafJobMessage(List<string> leaves)
        {
            return new BuildBakedLeavesMessage(projectName) { TileIds = leaves };
        }
    }
}
