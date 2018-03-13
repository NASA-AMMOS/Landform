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
        public PipelineCore Pipeline;
        public PipelineRoutine(PipelineCore pipeline)
        {
            Pipeline = pipeline;
        }

        public Image GetImage(ImageRef image)
        {
            return image.Load(Pipeline);
        }
        public ImageMetadata GetMetadata(ImageRef image)
        {
            return GetImage(image).Metadata;
        }

        public T Get<T>(string project, Guid guid, bool useCache = true) where T : DataProduct, new()
        {
            return Pipeline.Get<T>(project, guid, useCache);
        }


        public void Save(string project, DataProduct product, bool useCache = true)
        {
            Pipeline.Save(project, product, useCache);
        }

        public DynamoDBContext DynamoDB
        {
            get { return Pipeline.DynamoDB; }
        }
    }
}
