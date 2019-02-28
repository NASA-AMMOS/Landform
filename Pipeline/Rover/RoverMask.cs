using OPS.Imaging;
using OPS.RayTrace;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;

namespace OPS.Pipeline
{
    public class RoverMask
    {
        public static CuriosityRoverModel RoverModel = new CuriosityRoverModel();

        public static Image Build(Image image, int borderWidth = 10)
        {
            var metadata = image.Metadata as PDSMetadata;
            if (metadata == null)
            {
                return null;
            }

            PDSParser parser = new PDSParser(metadata);
            var posedRover = RoverModel.BuildMesh(parser.Articulation, !parser.IsHazcam);

            var sc = new SceneCaster();
            sc.AddMesh(posedRover, null, Matrix.Identity);
            sc.Build();

            var cmod = metadata.CameraModel;

            Image res = new Image(1, metadata.Width, metadata.Height);
            for (int i = 0; i < res.Width; i++)
            {
                for (int j = 0; j < res.Height; j++)
                {
                    var ray = cmod.Unproject(new Vector2(i, j));
                    res[0, j, i] = sc.Occludes(ray) ? 0 : 1;
                }
            }

            //corrects for missing data, often caused by undistorting an image
            if (parser.HasMissingConstant)
            {
                float[] missingVal = parser.MissingConstant.Select(x => (float)x).ToArray();
                for (int idxRow = 0; idxRow < res.Height; idxRow++)
                {
                    for (int idxCol = 0; idxCol < res.Width; idxCol++)
                    {
                        if (res.BandValuesEqual(idxRow, idxCol, missingVal))
                        {
                            res[0, idxRow, idxCol] = 0;
                        }
                    }
                }
            }

            borderWidth = Math.Min(res.Width / 2, borderWidth);
            for (int border = 0; border < borderWidth; border++)
            {
                //whole row
                for(int idxCol=0; idxCol < res.Width; idxCol++)
                {
                    res[0, border, idxCol] = 0;
                    res[0, res.Height - 1 - border, idxCol] = 0;
                }

                //whole column
                for (int idxRow = 0; idxRow < res.Height; idxRow++)
                {
                    res[0, idxRow, border] = 0;
                    res[0, idxRow, res.Width - 1 - border] = 0;
                }
            }

            return res;
        }
    }
}
