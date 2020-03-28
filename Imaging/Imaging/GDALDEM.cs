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
    public class GDALDEM : IDisposable
    {
        public Dataset Dataset { get; private set; }

        public int Width
        {
            get
            {
                return Dataset.RasterXSize;
            }
        }

        public int Height
        {
            get
            {
                return Dataset.RasterYSize;
            }
        }

        public DEMBody Body { get; private set; }

        private SpatialReference bodyFrame, projectedFrame;
        private CoordinateTransformation latLonToProjected, projectedToLatLon;

        private Matrix geoTransform;
        private Matrix invGeoTransform;

        public GDALDEM(string file, DEMBody body)
        {
            this.Dataset = Gdal.Open(file, Access.GA_ReadOnly);
            this.Body = body;

            double[] raw = new double[6];
            Dataset.GetGeoTransform(raw);

            //Xp = raw[0] + C*raw[1] + R*raw[2];
            //Yp = raw[3] + C*raw[4] + R*raw[5];

            //Xna matrix is row major
            geoTransform = new Matrix(raw[1], raw[4], 0, 0,
                                      raw[2], raw[5], 0, 0,
                                           0,      0, 1, 0,
                                      raw[0], raw[3], 0, 1);

            invGeoTransform = Matrix.Invert(geoTransform);

            bodyFrame = body.MakeSphericalSpatialReference();
            projectedFrame = new SpatialReference(Dataset.GetProjectionRef());

            latLonToProjected = new CoordinateTransformation(bodyFrame, projectedFrame);
            projectedToLatLon = new CoordinateTransformation(projectedFrame, bodyFrame);
        }

        public static GDALDEM Load(string file, string body)
        {
            switch(body.ToLower())
            {
                case "mars": return new GDALDEM(file, new MarsBody());
                case "earth": return new GDALDEM(file, new EarthBody());
                default: throw new Exception("orbital DEM for planetary body not supported: " + body);
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
        /// </summary>
        public Vector3 LatLonToXYZ(Vector3 bodyPos, double lat0 = 0, double lon0 = 0)
        {
            double r = Body.GetRadius() + bodyPos.Z;
            return new Vector3(
                r * Math.Sin((bodyPos.Y - lat0) * Math.PI / 180.0),
                r * Math.Cos((bodyPos.Y - lat0) * Math.PI / 180.0) * Math.Sin((bodyPos.X - lon0) * Math.PI / 180.0),
                r * Math.Cos((bodyPos.Y - lat0) * Math.PI / 180.0) * Math.Cos((bodyPos.X - lon0) * Math.PI / 180.0)
            );
        }

        /// <summary>
        /// X = longitude, Y = latitude
        /// </summary>
        public Vector3 XYZToLatLon(Vector3 worldPos, double lat0 = 0, double lon0 = 0)
        {
            double r = worldPos.Length();
            double lat = Math.Asin(worldPos.X / r);
            double lon = Math.Atan2(worldPos.Y, worldPos.Z);
            return new Vector3(lon * 180 / Math.PI + lon0, lat * 180 / Math.PI + lat0, r - Body.GetRadius());
        }

        public Vector3 XYZToImage(Vector3 worldPos, double lat0 = 0, double lon0 = 0)
        {
            return LatLonToImage(XYZToLatLon(worldPos, lat0: lat0, lon0: lon0));
        }

        public Vector3 ImageToXYZ(Vector3 imagePos, double lat0 = 0, double lon0 = 0)
        {
            return LatLonToXYZ(ImageToLatLon(imagePos), lat0: lat0, lon0: lon0);
        }

        private ConcurrentDictionary<Tuple<Vector2, int>, double> interpCache =
            new ConcurrentDictionary<Tuple<Vector2, int>, double>();

        /// <summary>
        /// X = longitude, Y = latitude
        /// </summary>
        public double InterpolateElevationAtLatLon(Vector2 latLon, int radius = 2)
        {
            return InterpolateElevationAtLatLon(latLon.Y, latLon.X);
        }

        public double InterpolateElevationAtLatLon(double lat, double lon, int radius = 2)
        {
            return interpCache.GetOrAdd(new Tuple<Vector2, int>(new Vector2(lon, lat), radius), _ => {

                    Vector3 px = LatLonToImage(new Vector3(lon, lat, 0.0));
                    
                    if (px.X < 0 || px.X >= Width || px.Y < 0 || px.Y >= Height)
                    {
                        throw new ArgumentException(string.Format("lat={0} lon={1} out of DEM bounds", lat, lon));
                    }
                    
                    int xl = (int)Math.Max(Math.Round(px.X - radius), 0);
                    int yl = (int)Math.Max(Math.Round(px.Y - radius), 0);
                    int xu = (int)Math.Min(Math.Round(px.X + radius), Width - 1);
                    int yu = (int)Math.Min(Math.Round(px.Y + radius), Height - 1);
                    int w = xu - xl + 1;
                    int h = yu - yl + 1;
                    
                    float[] window = new float[w * h];
                    double maskValue = 0;
                    int hasMaskValue = 0;

                    //though GDAL seems to claim to be MT safe, this does seem necessary
                    //another strategy may be to read the whole entire raster into a big managed array at construction
                    lock (this)
                    {
                        var band = Dataset.GetRasterBand(1);
                        band.ReadRaster(xl, yl, w, h, window, w, h, 0, 0);
                        band.GetNoDataValue(out maskValue, out hasMaskValue);
                    }
                    
                    double sum = 0;
                    int n = 0;
                    for (int i = 0; i < window.Length; i++)
                    {
                        if (hasMaskValue == 0 || window[i] != maskValue)
                        {
                            sum += window[i];
                            n++;
                        }
                    }
                    
                    return sum / n;
                });
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
                if (Dataset != null)
                {
                    Dataset.Dispose();
                    Dataset = null;
                }
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
