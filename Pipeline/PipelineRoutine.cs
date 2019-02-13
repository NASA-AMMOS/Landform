using Amazon.DynamoDBv2.DataModel;
using OPS.Imaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Pipeline
{
    public class PipelineRoutine
    {
        public readonly PipelineCore pipeline;

        public PipelineRoutine(PipelineCore pipeline)
        {
            this.pipeline = pipeline;
        }
    }
}
