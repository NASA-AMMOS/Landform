using Amazon.DynamoDBv2.DataModel;
using OPS.Imaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Plumbing
{
    public class PipelineRoutine
    {
        public readonly PipelineCore Pipeline;

        public PipelineRoutine(PipelineCore pipeline)
        {
            Pipeline = pipeline;
        }
    }
}
