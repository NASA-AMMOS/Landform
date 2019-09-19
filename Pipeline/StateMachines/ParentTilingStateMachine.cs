using System;
using System.Linq;
using System.Collections.Generic;
using log4net;
using OPS.Cloud;
using OPS.Pipeline.TilingServer;

namespace OPS.Pipeline
{
    class ParentTilingStateMachine : PipelineStateMachine
    {
        public ParentTilingStateMachine(PipelineCore pipeline, string projectName) : base(pipeline, projectName)
        { }

        protected override void RunProject()
        {
            LogInfo("building parents");
            RunProject(new BuildParentsMessage(projectName));
        }

        protected override QueueMessage MakeLeafJobMessage(List<string> leaves)
        {
            throw new NotImplementedException();
        }
    }
}
