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
        public SpatialReference WorldFrame;
        public SpatialReference ImageFrame;
        public Matrix GeoTransform;
        public Matrix InverseGeoTransform;

        CoordinateTransformation ImageToWorld;
        CoordinateTransformation WorldToImage;


        static string ExtractFullAttr(SpatialReference sr, string name, HashSet<string> toExpand = null)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(name);
            sb.Append('[');
            int i;
            for (i = 0; ; i++)
            {
                string attr = sr.GetAttrValue(name, i);
                if (attr == null) break;
                if (i != 0) sb.Append(',');
                if (toExpand != null && toExpand.Contains(attr))
                {
                    sb.Append(ExtractFullAttr(sr, attr));
                }
                else if (attr[0] >= '0' && attr[0] <= '9')
                {
                    sb.Append(attr);
                }
                else
                {
                    sb.Append('"');
                    sb.Append(attr);
                    sb.Append('"');
                }
            }
            if (i == 0) return null;
            sb.Append("]");
            return sb.ToString();
        }

        public GDALCameraModel(double[] geoTransform, string projection)
        {
            ImageFrame = new SpatialReference(projection);

            // come up with a sensible world frame
            var datum = ExtractFullAttr(ImageFrame, "DATUM", new HashSet<string> { "SPHEROID" });
            var primem = ExtractFullAttr(ImageFrame, "PRIMEM");
            if (primem == null)
            {
                primem = "PRIMEM[\"Generic\", 0]";
            }

            StringBuilder sb = new StringBuilder();
            sb.Append("GEOCCS[");
            sb.Append("\"Jeromy\",");
            sb.Append(datum); sb.Append(",");
            sb.Append(primem); sb.Append(",");
            sb.Append("ANGLEUNIT[\"degree\",0.0174532925199433],");
            sb.Append("LENGTHUNIT[\"metre\",1,AUTHORITY[\"EPSG\",\"9001\"]]");
            sb.Append("]");

            WorldFrame = new SpatialReference(sb.ToString());

            GeoTransform = Matrix.Identity;
            GeoTransform[0, 0] = geoTransform[1];
            GeoTransform[0, 1] = geoTransform[2];
            GeoTransform[0, 3] = geoTransform[0];
            GeoTransform[1, 0] = geoTransform[4];
            GeoTransform[1, 1] = geoTransform[5];
            GeoTransform[1, 3] = geoTransform[3];
            InverseGeoTransform = Matrix.Invert(GeoTransform);

            ImageToWorld = new CoordinateTransformation(ImageFrame, WorldFrame);
            WorldToImage = new CoordinateTransformation(WorldFrame, ImageFrame);
        }

        /// <summary>
        /// Copy constructor
        /// </summary>
        /// <param name="that"></param>
        public GDALCameraModel(GDALCameraModel that)
        {
            WorldFrame = that.WorldFrame.Clone();
            ImageFrame = that.ImageFrame.Clone();
            GeoTransform = that.GeoTransform;
            InverseGeoTransform = that.InverseGeoTransform;

            ImageToWorld = new CoordinateTransformation(ImageFrame, WorldFrame);
            WorldToImage = new CoordinateTransformation(WorldFrame, ImageFrame);
        }

        public override Vector2 Project(Vector3 pos, out double range)
        {
            double[] res = new double[3];
            WorldToImage.TransformPoint(res, pos.X, pos.Y, pos.Z);
            Vector3 inPixelSpace = Vector3.Transform(new Vector3(res[0], res[1], res[2]), InverseGeoTransform);
            range = inPixelSpace.Z;
            return new Vector2(inPixelSpace.X, inPixelSpace.Y);
        }

        public override void Unproject(ref Vector2 pixelPos, out Ray ray)
        {
            var pt0 = Unproject(pixelPos, 0.0);
            var pt1 = Unproject(pixelPos, 1.0);
            ray = new Ray(pt0, Vector3.Normalize(pt1 - pt0));
        }

        public override Vector3 Unproject(Vector2 pixelPos, double range)
        {
            Vector3 inProjSpace = Vector3.Transform(new Vector3(pixelPos.X, pixelPos.Y, range), GeoTransform);
            double[] res = new double[3];
            ImageToWorld.TransformPoint(res, inProjSpace.X, inProjSpace.Y, inProjSpace.Z);
            return new Vector3(res[0], res[1], res[2]);
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

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    if (ImageFrame != null)
                        ImageFrame.Dispose();
                    if (WorldFrame != null)
                        WorldFrame.Dispose();
                    if (ImageToWorld != null)
                        ImageToWorld.Dispose();
                    if (WorldToImage != null)
                        WorldToImage.Dispose();
                }
                disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
        #endregion
    }
}
