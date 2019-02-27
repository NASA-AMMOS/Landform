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

        public static Image Build(Image image)
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

            return res;
        }
    }
}
