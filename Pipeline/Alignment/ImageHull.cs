using Microsoft.Xna.Framework;
using OPS.Geometry;
using OPS.Imaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Pipeline.Alignment
{
    public class ImageHull : Computation
    {
        // Argument
        public ImageRef image;
        public override void Init(object arg)
        {
            image = (ImageRef)arg;
        }

        // Result
        public ConvexHull convexHull;

        public override void Compute(ComputationContext context)
        {
            var img = image.Image;
            var cameraModel = img.CameraModel;

            int xDiv, yDiv, zDiv;
            if (cameraModel.GetType() == typeof(CAHV))
            {
                // For special case of linear camera models, only use min-max pts
                xDiv = yDiv = zDiv = 2;
            }
            else
            {
                // otherwise, sample some intermediate points to cope with fisheye nonsense
                xDiv = yDiv = zDiv = 5;
            }

            // TODO: justify/elaborate
            double nearClip = 0.1;
            double farClip = 10;

            List<Vector3> verts = new List<Vector3>(xDiv * yDiv * zDiv);

            for (int i = 0; i < xDiv; i++)
            {
                double x = (img.Width - 1) * (i / (double)(xDiv - 1));
                for (int j = 0; j < yDiv; j++)
                {
                    double y = (img.Width - 1) * (j / (double)(xDiv - 1));
                    for (int k = 0; k < zDiv; k++)
                    {
                        double z = nearClip + (farClip - nearClip) * (k / (double)(zDiv - 1));

                        Vector3 pos = cameraModel.ProjectPoint(new Vector2(x, y), z);
                        verts.Add(pos);
                    }
                }
            }

            convexHull = new ConvexHull(verts);
        }
    }
}
