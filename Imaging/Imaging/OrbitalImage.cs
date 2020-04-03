using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.IO;
using Microsoft.Xna.Framework;
using OSGeo.GDAL;
using OSGeo.OSR;
using OPS.Util;

namespace OPS.Imaging
{
    public abstract class PlanetaryBody
    {
        public abstract string Name { get; }

        public abstract double Radius { get; }

        public abstract SpatialReference MakeSphericalSpatialReference();

        public static PlanetaryBody GetByName(string name)
        {
            switch (name.ToLower())
            {
                case "mars": return new MarsBody();
                case "earth": return new EarthBody();
                default: throw new Exception("planetary body not supported: " + name);
            }
        }
    }

    public class MarsBody : PlanetaryBody
    {
        public override string Name { get { return "Mars"; } }

        public override double Radius { get { return 3396190.0; } }

        public override SpatialReference MakeSphericalSpatialReference()
        {
            var ret = new SpatialReference(null);
            //ported from TerrainTools/ImageLib/Pipeline/Mars.cs
            ret.SetGeogCS("Mars Spherical", "SPHERICAL_MARS", "Mars",
                          Radius, 0, "Marsridian", 0.0, "Degree", Math.PI / 180.0);
            return ret;
        }
    }

    public class EarthBody : PlanetaryBody
    {
        public override string Name { get { return "Earth"; } }

        public override double Radius { get { return Osr.SRS_WGS84_SEMIMAJOR; } }

        public override SpatialReference MakeSphericalSpatialReference()
        {
            var ret = new SpatialReference(null);
            //https://gdal.org/tutorials/osr_api_tut.html#defining-a-geographic-coordinate-reference-system
            ret.SetGeogCS("Earth CRS", "World Geodetic System 1984", "Earth",
                          Osr.SRS_WGS84_SEMIMAJOR, Osr.SRS_WGS84_INVFLATTENING,
                          "Greenwich", 0.0, "Degree", Math.PI / 180.0);
            return ret;
        }
    }

    /// <summary>
    /// Not actually an Image.
    /// If you want to do the usual Image things, then you can also load the same file as a regular Image.
    /// This class provides helpers to translate between (lat, lon), planetary body frame, and image pixels.
    /// It uses GDAL to load metadata (only) from the file, which is typically a GeoTIFF.
    ///
    /// Planetary body frame origin is the center of the body.
    ///
    /// planetary body frame +X is lat=90deg, lon=0 (North)
    /// Planetary body frame +Y is lat=0, lon=90deg
    /// Planetary body frame +Z is lat=0, lon=0 (prime meridian at equator)
    ///
    /// Unfortunately this makes planetary body frame left handed.  I am not sure if this is a standard coordinate frame
    /// (at JPL, NASA, etc).  But I suspect it is entirely defined by our implementations of XYZToLatLon() and
    /// LatLonToXYZ(), which perform fairly standard cartesian to/from spherical conversions.  There is also
    /// PlacesDB.GetEstimatedLatLon(), which interprets the returned offset vector from PlacesDB as "X northing, Y
    /// easting".  So perhaps that is where this convention started.  Nevertheless, it is almost certianly possible for
    /// us to change our definition of planetary body frame so that it's right handed, e.g. with +Z north, +X pointing
    /// along the prime meridian at the equator, and +Y pointing to longitude 90deg east at the equator.  This would
    /// correspond with the IAU Working Group for Cartographic Coordinates and Rotational Elements
    /// (https://www.iau.org/public/images/detail/ann18010a/).
    /// TODO https://github.jpl.nasa.gov/OnSight/Landform/issues/1033
    /// </summary>
    public class OrbitalImage : IDisposable
    {
        public readonly int Bands;
        public readonly int Width;
        public readonly int Height;

        public readonly PlanetaryBody Body;

        public readonly string ProjectionRef;

        protected double[] rawGeoTransform;
        protected Dataset gdalDataset;

        private SpatialReference bodyFrame, projectedFrame;
        private CoordinateTransformation latLonToProjected, projectedToLatLon;

        private Matrix geoTransform;
        private Matrix invGeoTransform;

        public OrbitalImage(string file, PlanetaryBody body, bool disposeDataset = true)
        {
            this.Body = body;

            gdalDataset = Gdal.Open(file, Access.GA_ReadOnly);

            Bands = gdalDataset.RasterCount;
            Width = gdalDataset.RasterXSize;
            Height = gdalDataset.RasterYSize;

            //GDAL datasets have two ways of describing the relationship between
            //raster positions (in pixel/line coordinates) and georeferenced coordinates.
            //The first, and most commonly used is the affine transform (the other is GCPs).
            //https://gdal.org/user/raster_data_model.html
            //Note: we use affine below

            //Fetches the coefficients for transforming between pixel/line (C, R) raster space, 
            //and projection coordinates(Xp, Yp) space
            //The default transform is (0, 1, 0, 0, 0, 1) and should be returned even when a CE_Failure error 
            //is returned, such as for formats that don’t support transformation to projection coordinates.
            //from: https://gdal.org/api/gdaldataset_cpp.html

            var raw = rawGeoTransform = new double[6];
            gdalDataset.GetGeoTransform(raw);

            //Xp = raw[0] + C*raw[1] + R*raw[2];
            //Yp = raw[3] + C*raw[4] + R*raw[5];

            //In the particular, but common, case of a “north up” image without any rotation or shearing, 
            //the georeferencing transform takes the following form :
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
                                           0,      0, 1, 0,
                                      raw[0], raw[3], 0, 1);

            invGeoTransform = Matrix.Invert(geoTransform);

            bodyFrame = body.MakeSphericalSpatialReference();
            ProjectionRef = gdalDataset.GetProjectionRef();
            projectedFrame = new SpatialReference(ProjectionRef);

            latLonToProjected = new CoordinateTransformation(bodyFrame, projectedFrame);
            projectedToLatLon = new CoordinateTransformation(projectedFrame, bodyFrame);

            if (disposeDataset)
            {
                gdalDataset.Dispose();
                gdalDataset = null;
            }
        }

        public OrbitalImage(string file, string bodyName, bool disposeDataset = true) :
            this(file, PlanetaryBody.GetByName(bodyName), disposeDataset) { }

        public void Dump(ILogger logger)
        {
            var raw = rawGeoTransform;
            logger.LogInfo("GDAL GeoTransform:");
            logger.LogInfo("Xp = {0} + C * {1} + R * {2}", raw[0], raw[1], raw[2]);
            logger.LogInfo("Yp = {0} + C * {1} + R * {2}", raw[3], raw[4], raw[5]);
            logger.LogInfo("GDAL projection ref: {0}", ProjectionRef);
            logger.LogInfo("planetary body: {0}", Body.Name);
        }

        /// <summary>
        /// Uses ImageToXYZ() to construct a local coordinate frame at originPixel.
        /// Returns the location of the frame origin.
        /// Return vector right points rightward in image (direction of increasing column).
        /// Return vector down points downward in image (direction of increasing row).
        /// Return vector elevation points in the direction of increasing elevation.
        /// The return vectors are not necessarily unit vectors, orthogonal, or right-handed.
        /// </summary>
        public Vector3 GetLocalBasisInBodyFrame(Vector2 originPixel,
                                                out Vector3 elevation, out Vector3 right, out Vector3 down)
        {
            var originXYZ = ImageToXYZ(originPixel);

            var elevationXYZ = ImageToXYZ(new Vector3(originPixel.X, originPixel.Y, 1));
            elevation = elevationXYZ - originXYZ;

            var rightXYZ = ImageToXYZ(originPixel + new Vector2(1, 0));
            right = rightXYZ - originXYZ;

            var downXYZ = ImageToXYZ(originPixel + new Vector2(0, 1));
            down = downXYZ - originXYZ;

            return originXYZ;
        }

        /// <summary>
        /// The GDAL basis at a given pixel may not be quite uniform scale, because DEMs are often in equirectangular
        /// (equidistant cylindrical) projection with latitude_of_origin = 0 so at higher latitudes the effective
        /// horizontal meters per pixel is shorter.
        ///
        /// Less common projection types (not equirectangular) may have additional concerns
        /// * the gdal basis may have skew, i.e. right may not be perpendicular to down
        /// * the gdal basis may have roll, i.e. right may not be east in body.
        ///
        /// This function checks for those conditions, reports the relevant angles, and can throw exceptions if the
        /// skew or roll is significant.
        ///
        /// Note that many current mission systems currently assume equirectangular projection.  For example, from the
        /// PlacesDB user guide v2.0.b.002:
        /// > Orbital images must be in a rectangular projection, with northl up in the image. The line (Y) dimension
        /// > of the image is aligned north/south with the sample (X) dimension aligned east/west.
        /// > There is a constant pixel scale (meters/pixel) in both directions. The X and Y scales need not be the
        /// > same, but it is recommended they be.
        ///
        /// The MSL landing site, Gale crater, was 5.4deg S 137.8deg E.  The orbital assets were in equirectangular
        /// projection and did use latitude_of_origin = 0 because the latitude was considered low enough that the
        /// distortion wasn't significant and pixels could be considered square:
        /// https://github.jpl.nasa.gov/OnSight/Landform/issues/1031#issuecomment-271530.
        ///
        /// The M2020 landing site, Jezero crater, is 18.38deg N, 77.58deg E.  Orbital assets are still in
        /// equirectangular projection.  Some currently available orbital assets at the time of this writing appear to
        /// still be using latitude_of_origin = 0, though it is planned that during the mission the
        /// latitude_of_projection will be 18.4663.  Thus, using current assets, it may be necessary to account for
        /// horizontal distortion.  But using the final mission assets, again it should be valid to consider pixels
        /// square: https://github.jpl.nasa.gov/OnSight/Landform/issues/1031#issuecomment-271496.
        ///
        /// When using orbital images as DEMS, it may also be necesary to consider fall-off with distance because a
        /// tangent plane to a sphere gets further from the sphere the further away from the tangent point you walk. I
        /// calculate this as on the order of about 16cm at 1km from the tangent point for a planet the size of Mars.
        /// </summary>
        public double CheckBasisAndGetAspect(Vector2 originPixel, ILogger logger = null, bool throwOnError = false)
        {
            GetLocalBasisInBodyFrame(originPixel,
                                     out Vector3 gdalElevation, out Vector3 gdalRight, out Vector3 gdalDown);

            double gdalElevationScale = gdalElevation.Length();

            if (logger != null)
            {
                logger.LogInfo("gdal elevation scale: {0}", gdalElevationScale);
            }

            var gdalMetersPerPixel = new Vector2(gdalRight.Length(), gdalDown.Length());

            double gdalPixelAspect = gdalMetersPerPixel.X / gdalMetersPerPixel.Y;

            if (logger != null)
            {
                logger.LogInfo("gdal meters per pixel: {0}x, {1}y, aspect {2}",
                               gdalMetersPerPixel.X, gdalMetersPerPixel.Y, gdalPixelAspect);
            }

            gdalElevation = Vector3.Normalize(gdalElevation);
            gdalRight = Vector3.Normalize(gdalRight);
            gdalDown = Vector3.Normalize(gdalDown);

            double angle(Vector3 unitVecA, Vector3 unitVecB)
            {
                return Math.Atan2(Vector3.Cross(unitVecA, unitVecB).Length(), Vector3.Dot(unitVecA, unitVecB));
            }

            double rad2deg = 180 / Math.PI;

            double tolDeg = 1e-3;

            double skewAngleDeg = (angle(gdalRight, gdalDown) - 0.5 * Math.PI) * rad2deg;
            bool skewOK = skewAngleDeg < tolDeg;

            if (logger != null)
            {
                logger.LogInfo("gdal basis skew angle: {0}deg", skewAngleDeg);
            }

            if (!skewOK)
            {
                string msg = string.Format("gdal basis skew angle {0}deg > {1}", skewAngleDeg, tolDeg);
                if (logger != null)
                {
                    logger.LogWarn(msg);
                }
                if (throwOnError)
                {
                    throw new Exception(msg);
                }
            }
            
            Vector3 bodyNorth = Vector3.Normalize(LatLonToXYZ(new Vector2(0, 90)));

            if (logger != null)
            {
                logger.LogInfo("North (latitude 90) direction in body: ({0}, {1}, {2})",
                               bodyNorth.X, bodyNorth.Y, bodyNorth.Z);
                
                var meridian = Vector3.Normalize(LatLonToXYZ(new Vector2(0, 0)));
                logger.LogInfo("prime meridian (longitude 0) at equator direction in body: ({0}, {1}, {2})",
                               meridian.X, meridian.Y, meridian.Z);

                var meridian90 = Vector3.Normalize(LatLonToXYZ(new Vector2(90, 0)));
                logger.LogInfo("longitude 90 at equator direction in body: ({0}, {1}, {2})",
                               meridian90.X, meridian90.Y, meridian90.Z);

                var xLonLat = XYZToLatLon(new Vector3(1, 0, 0));
                var yLonLat = XYZToLatLon(new Vector3(0, 1, 0));
                var zLonLat = XYZToLatLon(new Vector3(0, 0, 1));
                logger.LogInfo("bodyframe +X: lat={0}, lon={1}", xLonLat.Y, xLonLat.X);
                logger.LogInfo("bodyframe +Y: lat={0}, lon={1}", yLonLat.Y, yLonLat.X);
                logger.LogInfo("bodyframe +Z: lat={0}, lon={1}", zLonLat.Y, zLonLat.X);
            }

            double rollAngleDeg = (angle(gdalRight, bodyNorth) - 0.5 * Math.PI) * rad2deg;
            bool rollOK = rollAngleDeg < tolDeg;

            if (logger != null)
            {
                logger.LogInfo("gdal basis roll angle: {0}deg", rollAngleDeg);
            }

            if (!rollOK)
            {
                string msg = string.Format("gdal basis roll angle {0}deg > {1}", rollAngleDeg, tolDeg);
                if (logger != null)
                {
                    logger.LogWarn(msg);
                }
                if (throwOnError)
                {
                    throw new Exception(msg);
                }
            }

            return gdalPixelAspect;
        }

        /// <summary>
        /// input: X = column, Y = row, Z = elevation above body radius
        /// output: X = longitude degrees, Y = latitude degrees, Z = elevation above body radius
        /// </summary>
        public Vector3 ImageToLatLon(Vector3 imgPos)
        {
            Vector3 inProjSpace = Vector3.Transform(imgPos, geoTransform);
            double[] res = new double[3];
            projectedToLatLon.TransformPoint(res, inProjSpace.X, inProjSpace.Y, inProjSpace.Z);
            return new Vector3(res[0], res[1], res[2]);
        }

        /// <summary>
        /// input: X = column, Y = row
        /// output: X = longitude degrees, Y = latitude degrees
        /// </summary>
        public Vector2 ImageToLatLon(Vector2 imgPos)
        {
            var tmp = ImageToLatLon(new Vector3(imgPos.X, imgPos.Y, 0));
            return new Vector2(tmp.X, tmp.Y);
        }

        /// <summary>
        /// input: X = longitude degrees, Y = latitude degrees, Z = elevation above body radius
        /// output X = col, Y = row, Z = elevation above body radius
        /// </summary>
        public Vector3 LatLonToImage(Vector3 bodyPos)
        {
            double[] res = new double[3];
            latLonToProjected.TransformPoint(res, bodyPos.X, bodyPos.Y, bodyPos.Z);
            Vector3 inPixelSpace = Vector3.Transform(new Vector3(res[0], res[1], res[2]), invGeoTransform);
            return inPixelSpace;
        }

        /// <summary>
        /// input: X = longitude degrees, Y = latitude degrees
        /// output: X = col, Y = row
        /// </summary>
        public Vector2 LatLonToImage(Vector2 latLon)
        {
            var tmp = LatLonToImage(new Vector3(latLon.X, latLon.Y, 0));
            return new Vector2(tmp.X, tmp.Y);
        }

        /// <summary>
        /// input: X = longitude degres, Y = latitude degrees, Z = elevation above body radius
        /// output: (X, Y, Z) in body frame
        /// </summary>
        public Vector3 LatLonToXYZ(Vector3 bodyPos, double lat0 = 0, double lon0 = 0)
        {
            double r = Body.Radius + bodyPos.Z;
            return new Vector3(
                r * Math.Sin((bodyPos.Y - lat0) * Math.PI / 180.0),
                r * Math.Cos((bodyPos.Y - lat0) * Math.PI / 180.0) * Math.Sin((bodyPos.X - lon0) * Math.PI / 180.0),
                r * Math.Cos((bodyPos.Y - lat0) * Math.PI / 180.0) * Math.Cos((bodyPos.X - lon0) * Math.PI / 180.0)
            );
        }

        /// <summary>
        /// input: X = longitude degrees, Y = latitude degrees
        /// output: (X, Y, Z) at body radius in body frame
        /// </summary>
        public Vector3 LatLonToXYZ(Vector2 bodyPos, double lat0 = 0, double lon0 = 0)
        {
            return LatLonToXYZ(new Vector3(bodyPos.X, bodyPos.Y, 0), lat0, lon0);
        }

        /// <summary>
        /// input: (X, Y, Z) in body frame
        /// output: X = longitude degrees, Y = latitude degrees, Z = elevation above body radius
        /// </summary>
        public Vector3 XYZToLatLon(Vector3 worldPos, double lat0 = 0, double lon0 = 0)
        {
            double r = worldPos.Length();
            double lat = Math.Asin(worldPos.X / r);
            double lon = Math.Atan2(worldPos.Y, worldPos.Z);
            return new Vector3(lon * 180 / Math.PI + lon0, lat * 180 / Math.PI + lat0, r - Body.Radius);
        }

        /// <summary>
        /// input: (X, Y, Z) in body frame
        /// outupt: X = longitude degrees, Y = latitude degrees
        /// </summary>
        public Vector2 XYZToLatLon2D(Vector3 worldPos, double lat0 = 0, double lon0 = 0)
        {
            var tmp = XYZToLatLon(worldPos, lat0, lon0);
            return new Vector2(tmp.X, tmp.Y);
        }

        /// <summary>
        /// input: (X, Y, Z) in body frame
        /// output: X = col, Y = row, Z = elevation above body radius
        /// </summary>
        public Vector3 XYZToImage(Vector3 worldPos, double lat0 = 0, double lon0 = 0)
        {
            return LatLonToImage(XYZToLatLon(worldPos, lat0: lat0, lon0: lon0));
        }

        /// <summary>
        /// input: X = col, Y = row, Z = elevation above body radius
        /// output: (X, Y, Z) in body frame
        /// </summary>
        public Vector3 ImageToXYZ(Vector3 imagePos, double lat0 = 0, double lon0 = 0)
        {
            return LatLonToXYZ(ImageToLatLon(imagePos), lat0: lat0, lon0: lon0);
        }

        /// <summary>
        /// input: X = col, Y = row
        /// output: (X, Y, Z) at body radius in body frame
        /// </summary>
        public Vector3 ImageToXYZ(Vector2 imagePos, double lat0 = 0, double lon0 = 0)
        {
            return ImageToXYZ(new Vector3(imagePos.X, imagePos.Y, 0), lat0, lon0);
        }

        /// <summary>
        /// Convenience function for measuring the size of an image pixel at the current location
        /// assumes each of the corners can have a different sampling rate (arbitrary quadrilateral)
        /// returns the shortest length, the best, finest, smallest number of meters per pixel
        /// input: (X, Y, Z) in body frame
        /// output: the minimum meters per pixel in the neighborhood of the pixel corresponding to bodyPos
        /// </summary>
        public double GetFinestEstimatedMetersPerPixelAtXYZ(Vector3 bodyPos)
        {
            Vector3 pixelColRow = XYZToImage(bodyPos);
            double left = Math.Floor(pixelColRow.X);
            double right = Math.Ceiling(pixelColRow.X);
            double top = Math.Floor(pixelColRow.Y);
            double bottom = Math.Ceiling(pixelColRow.Y);

            Vector3 upperLeftXYZ = ImageToXYZ(new Vector3(left, top, 0));
            Vector3 upperRightXYZ = ImageToXYZ(new Vector3(right, top, 0));
            Vector3 lowerLeftXYZ = ImageToXYZ(new Vector3(left, bottom, 0));
            Vector3 lowerRightXYZ = ImageToXYZ(new Vector3(right, bottom, 0));

            double minDistanceMeters = Vector3.Distance(upperLeftXYZ, upperRightXYZ);
            minDistanceMeters = Math.Min(minDistanceMeters, Vector3.Distance(lowerRightXYZ, upperRightXYZ));
            minDistanceMeters = Math.Min(minDistanceMeters, Vector3.Distance(lowerRightXYZ, lowerLeftXYZ));
            minDistanceMeters = Math.Min(minDistanceMeters, Vector3.Distance(upperLeftXYZ, lowerLeftXYZ));

            return minDistanceMeters;
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
                if (gdalDataset != null)
                {
                    gdalDataset.Dispose();
                    gdalDataset = null;
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

    /// <summary>
    /// Specialization of OrbitalImage for the case where the image has a single band of elevations.
    /// Also see OPS.Geometry.DEM which is a more general purpose DEM implementation that can be backed by any Image.
    /// </summary>
    public class OrbitalDEM : OrbitalImage
    {
        private ConcurrentDictionary<Tuple<Vector2, int>, double> interpCache =
            new ConcurrentDictionary<Tuple<Vector2, int>, double>();

        public OrbitalDEM(string file, PlanetaryBody body) : base (file, body, false)
        {
            if (Bands != 1)
            {
                throw new Exception("expected single band elevation image");
            }
        }

        public OrbitalDEM(string file, string bodyName) : base (file, bodyName, false)
        {
            if (Bands != 1)
            {
                throw new Exception("expected single band elevation image");
            }
        }

        /// <summary>
        /// input: X = longitude, Y = latitude
        /// radius is interpolation window size
        /// output: average of non-masked values in window about pixel corresponding to given lat/lon
        /// </summary>
        public double InterpolateElevationAtLatLon(Vector2 latLon, int radius = 2)
        {
            return InterpolateElevationAtLatLon(latLon.Y, latLon.X);
        }

        /// <summary>
        /// radius is interpolation window size
        /// output: average of non-masked values in window about pixel corresponding to given lat/lon
        /// </summary>
        public double InterpolateElevationAtLatLon(double lat, double lon, int radius = 2)
        {
            return interpCache.GetOrAdd(new Tuple<Vector2, int>(new Vector2(lon, lat), radius), _ =>
            {
                
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
                    var band = gdalDataset.GetRasterBand(1);
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
    }
}
