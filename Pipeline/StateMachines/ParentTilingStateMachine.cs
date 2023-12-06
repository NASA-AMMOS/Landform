using System;
using System.Collections.Generic;

namespace JPLOPS.Pipeline
{
    class ParentTilingStateMachine : PipelineStateMachine
    {
        public ParentTilingStateMachine(PipelineCore pipeline, string projectName) : base(pipeline, projectName)
        { }

        protected override void RunProject()
        {
            RunProject(BuildParents);
        }

        protected override PipelineMessage MakeLeafJobMessage(List<string> leaves)
        {
            throw new NotImplementedException();
        }
    }
}
