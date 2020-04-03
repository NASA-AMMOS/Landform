using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.IO;
using Microsoft.Xna.Framework;
using OSGeo.GDAL;
using OSGeo.OSR;

namespace OPS.Imaging
{
    //this class is initialized with a file and a body to return and  object and used for moving between 
    // GIS related coordinate frames
    public class GDALTransform : IDisposable
    {
        readonly public int Width;
        readonly public int Height;

        public DEMBody Body { get; private set; }

        private SpatialReference bodyFrame, projectedFrame;
        private CoordinateTransformation latLonToProjected, projectedToLatLon;

        private Matrix geoTransform;
        private Matrix invGeoTransform;

        public GDALTransform(string file, DEMBody body)
        {
            using (Dataset Dataset = Gdal.Open(file, Access.GA_ReadOnly))
            {
                this.Body = body;

                //GDAL datasets have two ways of describing the relationship between raster positions (in pixel/line coordinates) 
                // and georeferenced coordinates. The first, and most commonly used is the affine transform (the other is GCPs).
                //https://gdal.org/user/raster_data_model.html
                // Note: we use affine below

                //Fetches the coefficients for transforming between pixel / line(P, L) raster space, 
                //and projection coordinates(Xp, Yp) space
                //The default transform is (0, 1, 0, 0, 0, 1) and should be returned even when a CE_Failure error 
                //is returned, such as for formats that don’t support transformation to projection coordinates.
                //from: https://gdal.org/api/gdaldataset_cpp.html

                double[] raw = new double[6];
                Dataset.GetGeoTransform(raw);

                //Xp = raw[0] + C*raw[1] + R*raw[2];
                //Yp = raw[3] + C*raw[4] + R*raw[5];

                // In the particular, but common, case of a “north up” image without any rotation or shearing, 
                // the georeferencing transform takes the following form :
                //raw[0] /* top left x */
                //raw[1] /* w-e pixel resolution */
                //raw[2] /* 0 */
                //raw[3] /* top left y */
                //raw[4] /* 0 */
                //raw[5] /* n-s pixel resolution (negative value) */
                //https://gdal.org/tutorials/raster_api_tut.html

                //Xna matrix is row major
                geoTransform = new Matrix(raw[1], raw[4], 0, 0,
                                          raw[2], raw[5], 0, 0,
                                               0, 0, 1, 0,
                                          raw[0], raw[3], 0, 1);

                invGeoTransform = Matrix.Invert(geoTransform);

                bodyFrame = body.MakeSphericalSpatialReference();
                projectedFrame = new SpatialReference(Dataset.GetProjectionRef());

                latLonToProjected = new CoordinateTransformation(bodyFrame, projectedFrame);
                projectedToLatLon = new CoordinateTransformation(projectedFrame, bodyFrame);

                Width = Dataset.RasterXSize;
                Height = Dataset.RasterYSize;
            }
        }

        /// <summary>
        /// input: X = pixel column, Y = pixel row
        /// return: X = longitude, Y = latitude
        /// </summary>
        public Vector3 ImageToLatLon(Vector3 imgPos)
        {
            Vector3 inProjSpace = Vector3.Transform(imgPos, geoTransform);
            double[] res = new double[3];
            projectedToLatLon.TransformPoint(res, inProjSpace.X, inProjSpace.Y, inProjSpace.Z);
            return new Vector3(res[0], res[1], res[2]);
        }

        /// <summary>
        /// X = longitude, Y = latitude, Z = altitude => X = col, Y = row, Z = altitude
        /// </summary>
        public Vector3 LatLonToImage(Vector3 bodyPos)
        {
            double[] res = new double[3];
            latLonToProjected.TransformPoint(res, bodyPos.X, bodyPos.Y, bodyPos.Z);
            Vector3 inPixelSpace = Vector3.Transform(new Vector3(res[0], res[1], res[2]), invGeoTransform);
            return inPixelSpace;
        }

        /// <summary>
        /// X = longitude, Y = latitude => X = col, Y = row
        /// </summary>
        public Vector2 LatLonToImage(Vector2 latLon)
        {
            var tmp = LatLonToImage(new Vector3(latLon.X, latLon.Y, 0));
            return new Vector2(tmp.X, tmp.Y);
        }

        /// <summary>
        /// X = longitude, Y = latitude
        /// XYZ is a sphere in a left-handed axis convention with x to north pole; y though lon 90, lat 0 (equator); z through 0 lon, 0 lat (equator);
        /// /// </summary>
        public Vector2 LatLonToXYZ(Vector2 latlon, double lat0 = 0, double lon0 = 0)
        {
            double r = Body.GetRadius();
            return new Vector2(
                r * Math.Sin((latlon.Y - lat0) * Math.PI / 180.0),
                r * Math.Cos((latlon.Y - lat0) * Math.PI / 180.0) * Math.Sin((latlon.X - lon0) * Math.PI / 180.0)
            );
        }
        /// <summary>
        /// X = longitude, Y = latitude, Z = elevation relative to sphere
        /// XYZ is sphere in a left-handed axis convention  with x to north pole; y though lon 90, lat 0 (equator); z through 0 lon, 0 lat (equator);
        /// </summary>
        public Vector3 LatLonToXYZ(Vector3 lonlat, double lat0 = 0, double lon0 = 0)
        {
            double r = Body.GetRadius() + lonlat.Z;
            return new Vector3(
                r * Math.Sin((lonlat.Y - lat0) * Math.PI / 180.0),
                r * Math.Cos((lonlat.Y - lat0) * Math.PI / 180.0) * Math.Sin((lonlat.X - lon0) * Math.PI / 180.0),
                r * Math.Cos((lonlat.Y - lat0) * Math.PI / 180.0) * Math.Cos((lonlat.X - lon0) * Math.PI / 180.0)
            );
        }

        /// <summary>
        /// XYZ is sphere in a left-handed axis convention with x to north pole; y though lon 90, lat 0 (equator); z through 0 lon, 0 lat (equator);
        /// return X = longitude, Y = latitude
        /// </summary>
        public Vector3 XYZToLatLon(Vector3 worldPos, double lat0 = 0, double lon0 = 0)
        {
            double r = worldPos.Length();
            double lat = Math.Asin(worldPos.X / r);
            double lon = Math.Atan2(worldPos.Y, worldPos.Z);
            return new Vector3(lon * 180 / Math.PI + lon0, lat * 180 / Math.PI + lat0, r - Body.GetRadius());
        }

        /// <summary>
        /// XYZ is sphere in a left-handed axis convention with x to north pole; y though lon 90, lat 0 (equator); z through 0 lon, 0 lat (equator);
        /// </summary>
        public Vector3 XYZToImage(Vector3 worldPos, double lat0 = 0, double lon0 = 0)
        {
            return LatLonToImage(XYZToLatLon(worldPos, lat0: lat0, lon0: lon0));
        }

        /// <summary>
        /// XYZ is sphere in a left-handed axis convention with x to north pole; y though lon 90, lat 0 (equator); z through 0 lon, 0 lat (equator);
        /// </summary>
        public Vector3 ImageToXYZ(Vector3 imagePos, double lat0 = 0, double lon0 = 0)
        {
            return LatLonToXYZ(ImageToLatLon(imagePos), lat0: lat0, lon0: lon0);
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
                if (bodyFrame != null)
                {
                    bodyFrame.Dispose();
                    bodyFrame = null;
                }
                if (projectedFrame != null)
                {
                    projectedFrame.Dispose();
                    projectedFrame = null;
                }
                if (latLonToProjected != null)
                {
                    latLonToProjected.Dispose();
                    latLonToProjected = null;
                }
                if (projectedToLatLon != null)
                {
                    projectedToLatLon.Dispose();
                    projectedToLatLon = null;
                }
            }
        }
    }
}
