using System;
using System.Linq;
using System.Collections.Generic;
using log4net;
using OPS.Cloud;
using OPS.Pipeline.TilingServer;

namespace OPS.Pipeline
{
    class GenericTilingStateMachine : PipelineStateMachine
    {
        public GenericTilingStateMachine(PipelineCore pipeline, string projectName) : base(pipeline, projectName)
        { }

        protected override PipelineMessage MakeLeafJobMessage(List<string> leaves)
        {
            return new BuildLeavesMessage(projectName) { TileIds = leaves };
        }
    }
}
