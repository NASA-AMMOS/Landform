using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Util;
using OPS.Cloud;
using OPS.Pipeline;
using OPS.Imaging;

namespace OPS.Pipeline.AlignmentServer
{
    public class CreateMaskMessage : QueueMessage
    {
        public string ImageUrl;
        public CreateMaskMessage() { } 
        public CreateMaskMessage(string projectName) : base(projectName) { } 
    }

    public class MaskCreatedMessage : QueueMessage
    {
        public string ImageUrl;
        public Guid MaskGuid;
        public MaskCreatedMessage() { }
        public MaskCreatedMessage(string projectName) : base(projectName) { }
    }

    public class CreateMask : CloudPipelineOperation
    {
        private readonly  CreateMaskMessage message;

        public CreateMask(CloudPipeline pipeline, CreateMaskMessage message) : base(pipeline, message)
        {
            this.message = message;
        }

        public void Process()
        {
            var project = Project.Find(pipeline, projectName);
            pipeline.LogInfo("creating mask for image {0} in project {1}",
                             StringHelper.GetLastUrlPathSegment(message.ImageUrl), project.Name);
            var mask = new PngDataProduct(RoverMask.Build(pipeline.LoadImage(message.ImageUrl)));
            pipeline.SaveDataProduct(project.ProductPath, mask, projectName);
            pipeline.LogInfo("created mask for image {0} in project {1}",
                             StringHelper.GetLastUrlPathSegment(message.ImageUrl), project.Name);
            pipeline.MasterQueue.Enqueue(new MaskCreatedMessage(projectName)
                                         { ImageUrl = message.ImageUrl, MaskGuid = mask.Guid });
        }
    }
}
