using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;

namespace OPS.Imaging
{
    public class GDALCameraModel : CameraModel
    {
        public GDALCameraModel(double[] geoTransform, string projection)
        {

        }

        /// <summary>
        /// Copy constructor
        /// </summary>
        /// <param name="that"></param>
        public GDALCameraModel(GDALCameraModel that)
        {
        }             

        public override Vector2 Backproject(Vector3 pos, out double range)
        {
            throw new NotImplementedException();
        }

        public override void ProjectRay(ref Vector2 pixelPos, out Ray ray)
        {
            throw new NotImplementedException();
        }

        public override object Clone()
        {
            return new GDALCameraModel(this);
        }

        public override bool Linear
        {
            get
            {
                return false;
            }
        }
    }
}
