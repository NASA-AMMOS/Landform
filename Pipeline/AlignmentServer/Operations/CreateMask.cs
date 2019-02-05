using OPS.Cloud;
using OPS.Pipeline;
using OPS.Imaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
        private static readonly int borderMaxWidth = 10;

        private readonly CreateMaskMessage message;

        public CreateMask(CloudPipeline pipeline, CreateMaskMessage message) : base(pipeline, message)
        {
            this.message = message;
        }

        public void Process()
        {
            var mask = new PngDataProduct(MakeMask(pipeline.LoadImage(message.ImageUrl)));
            var project = Project.Find(pipeline, projectName);
            pipeline.SaveDataProduct(project.ProductPath, mask, projectName);
            pipeline.MasterQueue.Enqueue(new MaskCreatedMessage(projectName)
            {
                ImageUrl = message.ImageUrl,
                MaskGuid = mask.Guid
            });
        }

        private static Imaging.Image MakeMask(Imaging.Image img)
        {
            var mask = RoverMask.Build(img);

            //corrects for missing data, often caused by undistorting an image
            PDSParser parser = new PDSParser(img.Metadata as PDSMetadata);
            if (parser.HasMissingConstant)
            {
                float[] missingVal = parser.MissingConstant.Select(x => (float)x).ToArray();
                for (int idxRow = 0; idxRow < img.Height; idxRow++)
                {
                    for (int idxCol = 0; idxCol < img.Width; idxCol++)
                    {
                        if (img.BandValuesEqual(idxRow, idxCol, missingVal))
                        {
                            mask[0, idxRow, idxCol] = 0;
                        }
                    }
                }
            }

            int borderPx = Math.Min(mask.Width / 2, borderMaxWidth);
            for (int border = 0; border < borderPx; border++)
            {
                //whole row
                for(int idxCol=0;idxCol<mask.Width; idxCol++)
                {
                    mask[0, border, idxCol] = 0;
                    mask[0, mask.Height - 1 - border, idxCol] = 0;
                }

                //whole column
                for (int idxRow = 0; idxRow < mask.Height; idxRow++)
                {
                    mask[0, idxRow, border] = 0;
                    mask[0, idxRow, mask.Width - 1 - border] = 0;
                }
            }

            return mask;
        }
    }
}
