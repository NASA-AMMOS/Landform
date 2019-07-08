using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Util;
using OPS.Pipeline;
using OPS.Imaging;

//TODO: refactor so that local codepath does not have cloud dependencies
//https://github.jpl.nasa.gov/OnSight/Landform/issues/596
using QueueMessage = OPS.Cloud.QueueMessage;

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

    public class CreateMask : PipelineOperation
    {
        private readonly  CreateMaskMessage message;

        public CreateMask(PipelineCore pipeline, CreateMaskMessage message) : base(pipeline, message)
        {
            this.message = message;
        }

        public void Process()
        {
            var project = Project.Find(pipeline, projectName);
            var mission = MissionSpecific.GetInstance(project.Mission);
            var masker = mission.GetMasker();
            var shortUrl = StringHelper.GetLastUrlPathSegment(message.ImageUrl);
            pipeline.LogInfo("creating mask for image {0} in project {1}", shortUrl, project.Name);
            var mask = new PngDataProduct(masker.Build(pipeline.LoadImage(message.ImageUrl)));
            pipeline.SaveDataProduct(project.ProductPath, mask, projectName);
            pipeline.LogInfo("created mask for image {0} in project {1}", shortUrl, project.Name);
            pipeline.EnqueueToMaster(new MaskCreatedMessage(projectName)
                                     { ImageUrl = message.ImageUrl, MaskGuid = mask.Guid });
        }
    }
}
