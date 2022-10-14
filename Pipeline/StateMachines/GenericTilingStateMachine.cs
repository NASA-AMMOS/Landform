using System.Collections.Generic;
using JPLOPS.Pipeline.TilingServer;

namespace JPLOPS.Pipeline
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
