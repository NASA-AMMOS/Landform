using OPS.Cloud;
using OPS.Pipeline.TileServer;
using OPS.Plumbing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Pipeline.AlignmentServer
{
    public class CreateMask
    {
        public CreateMaskMessage Message;
        public PipelineCore Pipeline;
        public TileServerCloud Cloud;
        public CreateMask(CreateMaskMessage message, PipelineCore pipeline, TileServerCloud cloud)
        {
            Message = message;
            Pipeline = pipeline;
            Cloud = cloud;
        }

        public void Process()
        {
            var imgRef = Message.Image;
            var img = Pipeline.Load(imgRef);

            var mask = RoverMask.Build(img);
            int borderPx = Math.Min(mask.Width / 2, 10);
            for (int border = 0; border < borderPx; border++)
            {
                mask[0, border, 0] = 0;
                mask[0, 0, border] = 0;
                mask[0, mask.Height - 1 - border, 0] = 0;
                mask[0, 0, mask.Width - 1 - border] = 0;
            }
            var maskProd = new PngDataProduct(mask);
            Pipeline.Save(Message.Project, maskProd);

            Cloud.MasterQueue.Enqueue(new MaskCreatedMessage()
            {
                Image = Message.Image,
                Project = Message.Project,
                MaskGuid = maskProd.Guid
            });
        }
    }
}
