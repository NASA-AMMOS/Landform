using System;
using System.Linq;
using System.Collections.Generic;
using log4net;
using JPLOPS.Cloud;
using JPLOPS.Pipeline.TilingServer;

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
