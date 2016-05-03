using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;

using OSGeo.OSR;
using OSGeo.GDAL;

namespace OPS.Imaging
{
    public class GDALCameraModel : CameraModel, IDisposable
    {

        public readonly double[] GeoTransform;
        public readonly string Projection;
        
        Matrix geoMatrix;
        Matrix inverseGeoMatrix;
        SpatialReference projectedFrame;

        public GDALCameraModel(double[] geoTransform, string projection)
        {
            this.GeoTransform = new double[6];
            Array.Copy(geoTransform, this.GeoTransform, 6);
            this.Projection = projection;

            geoMatrix = new Matrix(geoTransform[1], geoTransform[4], 0, 0,
                                   geoTransform[2], geoTransform[5], 0, 0,
                                   0, 0, 1, 0,
                                   geoTransform[0], geoTransform[3], 0, 1);
            inverseGeoMatrix = Matrix.Invert(geoMatrix);
            projectedFrame = new SpatialReference(projection);
        }
             

        public override Vector2 Backproject(Vector3 pos, out double range)
        {
            throw new NotImplementedException();
        }

        public override void ProjectRay(ref Vector2 pixelPos, out Ray ray)
        {
            throw new NotImplementedException();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool isDisposing)
        {
            if (isDisposing)
            {
                if (projectedFrame != null)
                {
                    projectedFrame.Dispose();
                }
            }
        }

        public override object Clone()
        {
            return new GDALCameraModel(this.GeoTransform, this.Projection);
        }
    }
}
