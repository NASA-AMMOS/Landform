using OPS.Cloud;
using OPS.Pipeline.TileServer;
using OPS.Plumbing;
using OPS.Imaging;
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

        //settings
        private static readonly int borderMaxWidth = 10;

        public void Process()
        {
            var imgRef = Message.Image;
            var img = Pipeline.Load(imgRef);
            Imaging.Image mask = MakeMask(img);
            var maskProd = new PngDataProduct(mask);
            Pipeline.Save(Message.Project, maskProd);

            Cloud.MasterQueue.Enqueue(new MaskCreatedMessage()
            {
                Image = Message.Image,
                Project = Message.Project,
                MaskGuid = maskProd.Guid
            });
        }

        public static Imaging.Image MakeMask(Imaging.Image img)
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
