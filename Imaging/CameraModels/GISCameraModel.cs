using System;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using OSGeo.GDAL;
using OSGeo.OSR;
using JPLOPS.Util;
using JPLOPS.MathExtensions;

namespace JPLOPS.Imaging
{
    /// <summary>
    /// This class provides helpers to translate between (lon, lat), (easting, northing), planetary body frame, and GIS
    /// image pixels.
    ///
    /// Optionally uses GDAL to load metadata from a GeoTIFF file.
    ///
    /// Standard parallel is equator by default but missions not landing close to the equator may use
    /// equirectangular projections with a different standard parallel to get approximately square pixels in the DEM.
    ///
    /// Easting is distance along standard parallel from longitude=0.
    ///
    /// Northing is distance north of equator along a meridian.
    ///
    /// For JPL missions it's usually safe and correct to assume for GeoTIFF orbital assets that
    /// 1) for locations within some kilometers of the landing site, pixels will be effectively square (and most
    /// mission tools make that assumption)
    /// 2) north is always "up" in the image and east is always "right"
    /// 3) easting meters are uniform across all image rows
    /// 4) northing meters are uniform across all image columns.
    ///
    /// Occasionally we may work with locations in equirectangular GeoTIFF assets far from the standard parallel at
    /// which they were projected.  In such a case the assumption that pixels are square may be violated.  However, this
    /// can be accomodated by computing the pixel aspect ratio (ratio of width to height) which will be less than 1.
    /// The function CheckLocalGISImageBasisAndGetResolution() will both validate that the projection is equirectangular
    /// and also return the effective resolution.  Also see DEM2Mesh.CheckPlanarity().
    ///
    /// It is recommended to avoid lon/lat and body frame coordinates.  Rather, stick to easting/northing and pixels.
    /// The recommended way to get the pixel location of a sitedrive is to call
    /// EastingNorthingToImage(PlacesDB.GetEastingNorthing(siteDrive)).
    ///
    /// Planetary body frame origin is the center of the body.
    ///
    /// Planetary body frame +X is lat=0, lon=0
    /// Planetary body frame +Y is lat=0, lon=90deg
    /// planetary body frame +Z is lat=90deg, lon=0 (North)
    ///
    /// Corresponds to IAU Working Group for Cartographic Coordinates and Rotational Elements
    /// (https://www.iau.org/public/images/detail/ann18010a/).
    ///
    /// NOTE: Few if any other mission systems use this (or any planet-scale) frame, and nominal Landform codepaths
    /// should and probably can avoid it as well.
    ///
    /// As a camera model, the "image" is a raster in some GIS projection. It's pixels correspond to a grid of points
    /// laid out on the reference planetary surface (sphere). The origin of each pixel ray is a point on that surface.
    /// The direction of each pixel ray is the outward pointing unit normal to that surface at that point.
    /// </summary>
    public class GISCameraModel : ConformalCameraModel, IDisposable
    {
        public string BodyName { get { return Body.Name; } } //for json serialization

        [JsonIgnore]
        public PlanetaryBody Body { get; private set; }

        public int Bits { get; private set; }
        public int Bands { get; private set; }

        //example:
        //PROJCS["Equirectangular Mars 2000 Sphere IAU",
        //       GEOGCS["D_Mars_2000_Sphere",DATUM["D_Mars_2000_Sphere",SPHEROID["Mars_2000_Sphere_IAU",3396190,0]],
        //              PRIMEM["Reference_Meridian",0],UNIT["degree",0.0174532925199433]],PROJECTION["Equirectangular"],
        //              PARAMETER["latitude_of_origin",0],PARAMETER["central_meridian",0],
        //              PARAMETER["standard_parallel_1",0],PARAMETER["false_easting",0],PARAMETER["false_northing",0],
        //              UNIT["metre",1,AUTHORITY["EPSG","9001"]]]
        public string ProjectionRef { get; private set; }

        //ULC = upper left corner
        public Vector2 ULCEastingNorthing
        {
            get
            {
                return new Vector2(colRowToEastingNorthing[0], colRowToEastingNorthing[3]);
            }

            set
            {
                colRowToEastingNorthing[0] = value.X;
                colRowToEastingNorthing[3] = value.Y;
                InitColRowToEastingNorthing();
            }
        }

        private int width, height;

        //GDAL has two ways of describing the relationship between raster positions and georeferenced coordinates.
        //
        //The first, and most commonly used is the affine transform (the other is GCPs).
        //
        //https://gdal.org/user/raster_data_model.html
        //
        //Xp = affineTransform[0] + C * affineTransform[1] + R * affineTransform[2];
        //Yp = affineTransform[3] + C * affineTransform[4] + R * affineTransform[5];
        //
        //In the particular, but common, case of a "north up" image without any rotation or shearing, 
        //the georeferencing transform takes the following form: (https://gdal.org/tutorials/raster_api_tut.html)
        //
        //colRowToEastingNorthing[0] = top left x
        //colRowToEastingNorthing[1] = w-e pixel resolution
        //colRowToEastingNorthing[2] = 0
        //colRowToEastingNorthing[3] = top left y
        //colRowToEastingNorthing[4] = 0
        //colRowToEastingNorthing[5] = n-s pixel resolution (negative value)
        private double[] colRowToEastingNorthing = new double[6];

        private double standardParallelDegrees;

        private Matrix colRowElevToEastingNorthingElev, eastingNorthingElevToColRowElev;

        //(longitude degrees, latitude degrees, elevation meters) -> (easting, northing, elevation) meters
        private CoordinateTransformation lonLatElevToEastingNorthingElev;

        //(easting, northing, elevation) meters -> (longitude degrees, latitude degrees, elevation meters)
        private CoordinateTransformation eastingNorthingElevToLonLatElev;

        public GISCameraModel(string geoTiff, string bodyName)
        {
            this.Body = PlanetaryBody.GetByName(bodyName);

            using (var gdalDataset = Gdal.Open(geoTiff, Access.GA_ReadOnly))
            {
                Bits = GDALSerializer.GetBitDepth(gdalDataset);
                Bands = gdalDataset.RasterCount;
                width = gdalDataset.RasterXSize;
                height = gdalDataset.RasterYSize;
                
                ProjectionRef = gdalDataset.GetProjectionRef();
                
                //The default transform is (0, 1, 0, 0, 0, 1) and should be returned even when a CE_Failure error 
                //is returned, such as for formats that don't support transformation to projection coordinates.
                //from: https://gdal.org/api/gdaldataset_cpp.html
                gdalDataset.GetGeoTransform(colRowToEastingNorthing);

                if (colRowToEastingNorthing[2] != 0 || colRowToEastingNorthing[4] != 0)
                {
                    throw new Exception("skew detected in GeoTransform, only Equirectangular projection supported");
                }
            }
            Init();
        }

        [JsonConstructor]
        public GISCameraModel(string bodyName, int bands, int width, int height, string projectionRef,
                              Vector2 ulcEastingNorthing, Vector2 metersPerPixel)
        {
            this.Body = PlanetaryBody.GetByName(bodyName);
            this.Bands = bands;
            this.width = width;
            this.height = height;
            this.ProjectionRef = projectionRef;
            this.colRowToEastingNorthing[0] = ulcEastingNorthing.X;
            this.colRowToEastingNorthing[1] = metersPerPixel.X;
            this.colRowToEastingNorthing[2] = 0;
            this.colRowToEastingNorthing[3] = ulcEastingNorthing.Y;
            this.colRowToEastingNorthing[4] = 0;
            this.colRowToEastingNorthing[5] = metersPerPixel.Y;
            Init();
        }

        private void Init()
        {
            var projectionSpatialRef = new SpatialReference(ProjectionRef);
            
            var sphericalBodySpatialRef = Body.MakeSphericalSpatialReference();

            lonLatElevToEastingNorthingElev =
                new CoordinateTransformation(sphericalBodySpatialRef, projectionSpatialRef);
            
            eastingNorthingElevToLonLatElev =
                new CoordinateTransformation(projectionSpatialRef, sphericalBodySpatialRef);

            standardParallelDegrees = projectionSpatialRef.GetProjParm("standard_parallel_1", 0);

            InitColRowToEastingNorthing();
        }

        private void InitColRowToEastingNorthing()
        {
            //Xna matrix is row major
            colRowElevToEastingNorthingElev = new Matrix(colRowToEastingNorthing[1], colRowToEastingNorthing[4], 0, 0,
                                                         colRowToEastingNorthing[2], colRowToEastingNorthing[5], 0, 0,
                                                         0,                          0,                          1, 0,
                                                         colRowToEastingNorthing[0], colRowToEastingNorthing[3], 0, 1);
            
            eastingNorthingElevToColRowElev = Matrix.Invert(colRowElevToEastingNorthingElev);
        }

        public void Dump(ILogger logger, string prefix = null)
        {
            prefix = prefix ?? "";
            var x = colRowToEastingNorthing;
            logger.LogInfo("{0}{1} bits, {2} bands, {3}x{4} pixels, {5:f3}x{6:f3}m",
                           prefix, Bits, Bands, Width, Height, WidthMeters, HeightMeters);
            logger.LogInfo("Geotransform: GT(0)={0}, GT(1)={1}, GT(2)={2}, GT(3)={3}, GT(4)={4}, GT(5)={5}",
                           x[0], x[1], x[2], x[3], x[4], x[5]); //https://gdal.org/tutorials/geotransforms_tut.html
            logger.LogInfo("standard parallel: {0:f7}deg", standardParallelDegrees);
            logger.LogInfo("{0}easting  = {1} + col * {2} + row * {3}", prefix, x[0], x[1], x[2]);
            logger.LogInfo("{0}northing = {1} + col * {2} + row * {3}", prefix, x[3], x[4], x[5]);
            logger.LogInfo("{0}projection ref: {1}", prefix, ProjectionRef);
            logger.LogInfo("{0}planetary body: {1}", prefix, BodyName);
        }

        /* start CameraModel implementation ***************************************************************************/

        private Matrix bodyToLocal = Matrix.Identity, localToBody = Matrix.Identity;

        [JsonIgnore]
        public Matrix BodyToLocal
        {
            get
            {
                return bodyToLocal;
            }

            set
            {
                bodyToLocal = value;
                localToBody = Matrix.Invert(value);
            }
        }

        [JsonConverter(typeof(XNAMatrixJsonConverter))]
        public Matrix LocalToBody
        {
            get
            {
                return localToBody;
            }

            set
            {
                localToBody = value;
                bodyToLocal = Matrix.Invert(value);
            }
        }

        [JsonIgnore]
        public override bool Linear { get { return false; } }

        public override int Width { get { return width; } }
        public override int Height { get { return height; } }

        public override Vector2 MetersPerPixel
        {
            get
            {
                return new Vector2(colRowToEastingNorthing[1], Math.Abs(colRowToEastingNorthing[5]));
            }

            set
            {
                colRowToEastingNorthing[1] = value.X;
                colRowToEastingNorthing[5] = Math.Sign(colRowToEastingNorthing[5]) * value.Y;
                InitColRowToEastingNorthing();
            }
        }

        /// <summary>
        /// Get a unit vector along the GIS image plane normal at the origin pixel in local frame, or the center pixel
        /// if local frame is planetary body frame.
        ///
        /// Local frame defaults to planetary body frame.  If you want something different, set LocalToBody
        /// to a transform taking points in the desired frame to planetary body frame (or BodyToLocal to the
        /// inverse).
        ///
        /// NOTE: See class header comments for definition of planetary body frame. Few if any other mission systems use
        /// this (or any planet-scale) frame, and nominal Landform codepaths should and probably can avoid it as well.
        /// </summary>
        [JsonIgnore]
        public override Vector3 ImagePlaneNormal
        {
            get
            {
                var ctr = LocalToBody != Matrix.Identity ? Project(Vector3.Zero) : 0.5 * new Vector2(Width, Height);
                return Unproject(ctr).Direction;
            }
        }

        /// <summary>
        /// Get a ray in local frame corresponding to a given pixel.
        ///
        /// The origin of the ray is a point on the planetary reference surface (i.e. at elevation 0).
        /// The direction of the ray is zenith.
        ///
        /// Local frame defaults to planetary body frame.  If you want something different, set LocalToBody
        /// to a transform taking points in the desired frame to planetary body frame (or BodyToLocal to the
        /// inverse).
        ///
        /// NOTE: See class header comments for definition of planetary body frame. Few if any other mission systems use
        /// this (or any planet-scale) frame, and nominal Landform codepaths should and probably can avoid it as well.
        /// </summary>
        public override void Unproject(ref Vector2 pixel, out Ray ray)
        {
            var rayOrigin = ImageToXYZ(new Vector3(pixel.X, pixel.Y, 0));
            ray = new Ray(Vector3.Transform(rayOrigin, bodyToLocal),
                          Vector3.Normalize(Vector3.TransformNormal(Vector3.Normalize(rayOrigin), bodyToLocal)));
        }

        /// <summary>
        /// Get a pixel corresponding to a point in local frame.
        ///
        /// Range is the elevation of the point above the planetary reference surface.
        ///
        /// Local frame defaults to planetary body frame.  If you want something different, set LocalToBody
        /// to a transform taking points in the desired frame to planetary body frame (or BodyToLocal to the
        /// inverse).
        ///
        /// NOTE: See class header comments for definition of planetary body frame. Few if any other mission systems use
        /// this (or any planet-scale) frame, and nominal Landform codepaths should and probably can avoid it as well.
        /// </summary>
        public override Vector2 Project(Vector3 point, out double range)
        {
            Vector3 colRowElev = XYZToImage(Vector3.Transform(point, localToBody));
            range = colRowElev.Z;
            return new Vector2(colRowElev.X, colRowElev.Y);
        }

        public override object Clone()
        {
            return (GISCameraModel)MemberwiseClone();
        }

        public override ConformalCameraModel Decimated(int blocksize)
        {
            return new GISCameraModel(BodyName, Bands, Width / blocksize, Height / blocksize,
                                      ProjectionRef, ULCEastingNorthing, MetersPerPixel * blocksize);
        }

        /* end CameraModel implementation *****************************************************************************/

        /// <summary>
        /// Uses ImageToXYZ() to construct a local GIS image coordinate frame basis in body frame at originPixel.
        ///
        /// Returns the XYZ location of the local frame origin in planetary body frame.  This will be a point on the
        /// planetary reference surface (i.e. at elevation 0) at the lon/lat corresponding to originPixel.
        ///
        /// The image plane is the tangent plane to the planetary reference surface at that point.
        ///
        /// Return "right" vector is in direction of increasing column in image and has length equal to the pixel width.
        /// Return "down" vector is in direction of increasing row in image and has length equal to pixel height.
        /// Return "elevation" vector points in the direction of increasing elevation (zenith).
        ///
        /// These are not necessarily unit vectors, orthogonal, or right-handed, but see
        /// CheckLocalGISImageBasisAndGetResolution().
        ///
        /// NOTE: See class header comments for definition of planetary body frame. Few if any other mission systems use
        /// this (or any planet-scale) frame, and nominal Landform codepaths should and probably can avoid it as well.
        ///
        /// The origin point and basis vectors could be assembled into a row major 4x4 transform that takes a pixel
        /// location in the image to planetary body frame like this:
        ///
        /// originXYZ = GetLocalImageBasisInBodyFrame(originPixel, out elevation, out right, out down)
        ///
        /// [relCol, relRow, elevMeters, 1] * [ right,     0 ] = [bodyXMeters, bodyYMeters, bodyZMeters, 1]
        ///                                   [ down,      0 ]
        ///                                   [ elevation, 0 ]
        ///                                   [ originXYZ, 1 ]
        ///
        /// where (relCol, relRow) = (pixelCol, pixelRow) - originPixel.
        /// </summary>
        public Vector3 GetLocalGISImageBasisInBodyFrame(Vector2 originPixel,
                                                        out Vector3 elevation, out Vector3 right, out Vector3 down)
        {
            var origin = ImageToXYZ(originPixel);
            elevation = ImageToXYZ(new Vector3(originPixel.X, originPixel.Y, 1)) - origin;
            right = ImageToXYZ(originPixel + new Vector2(1, 0)) - origin;
            down = ImageToXYZ(originPixel + new Vector2(0, 1)) - origin;
            return origin;
        }

        /// <summary>
        /// Construct a LOCAL_LEVEL coordinate frame basis in body frame at originPixel, originElevaton.
        ///
        /// Mission surface LOCAL_LEVEL frame is typically +X north, +Y east, +Z nadir.
        ///
        /// Those are the defaults for the input basis, but for mission independent code call
        /// MissionSpecific.GetLocalLevelBasis() first.
        ///
        /// NOTE: See class header comments for definition of planetary body frame. Few if any other mission systems use
        /// this (or any planet-scale) frame, and nominal Landform codepaths should and probably can avoid it as well.
        /// </summary>
        public Matrix GetLocalLevelToBodyTransform(Vector2 originPixel,
                                                   Vector3 northInLocal, Vector3 eastInLocal, Vector3 nadirInLocal,
                                                   double originElevation = 0)
        {
            var localOriginInBody = ImageToXYZ(new Vector3(originPixel.X, originPixel.Y, originElevation));

            Vector3 bodyNorth = Vector3.Normalize(LonLatToXYZ(new Vector2(0, 90)));

            var localZenithInBody = Vector3.Normalize(localOriginInBody);
            var localNadirInBody = -localZenithInBody;

            var localEastInBody = Vector3.Cross(bodyNorth, localZenithInBody);

            if (localEastInBody.Length() < 1e-6) //corner case, localOriginInBody is at a pole
            {
                localEastInBody = Vector3.Normalize(LonLatToXYZ(new Vector2(90, 0))); 
            }
            else
            {
                localEastInBody = Vector3.Normalize(localEastInBody);
            }

            var localNorthInBody = Vector3.Normalize(Vector3.Cross(localZenithInBody, localEastInBody));

            var northEastNadirToLocal = new Matrix(northInLocal.X, northInLocal.Y, northInLocal.Z, 0,
                                                   eastInLocal.X,  eastInLocal.Y,  eastInLocal.Z, 0,
                                                   nadirInLocal.X, nadirInLocal.Y, nadirInLocal.Z, 0,
                                                   0, 0, 0, 1);
                                                   
            var northEastNadirToBody =
                new Matrix(localNorthInBody.X,  localNorthInBody.Y,  localNorthInBody.Z,  0,
                           localEastInBody.X,   localEastInBody.Y,   localEastInBody.Z,   0,
                           localNadirInBody.X,  localNadirInBody.Y,  localNadirInBody.Z,  0,
                           localOriginInBody.X, localOriginInBody.Y, localOriginInBody.Z, 1);
            
            return Matrix.Invert(northEastNadirToLocal) * northEastNadirToBody;
        }

        public Matrix GetLocalLevelToBodyTransform(Vector2 originPixel)
        {
            var northInLocal = new Vector3(1, 0, 0);
            var eastInLocal = new Vector3(0, 1, 0);
            var nadirInLocal = new Vector3(0, 0, 1);
            return GetLocalLevelToBodyTransform(originPixel, northInLocal, eastInLocal, nadirInLocal);
        }

        /// <summary>
        /// The GIS image frame basis at a given pixel may not be quite uniform scale, because DEMs are often in
        /// equirectangular (equidistant cylindrical) projection with latitude_of_origin = 0 so at higher latitudes the
        /// effective horizontal meters per pixel is shorter.
        ///
        /// Less common projection types (not equirectangular) may have additional concerns
        /// * the gdal basis may have skew, i.e. right may not be perpendicular to down
        /// * the gdal basis may have roll, i.e. right may not be east in body.
        ///
        /// This function checks for those conditions, reports the relevant angles, and can throw exceptions if the skew
        /// or roll is significant.  It returns the effective meters per pixel.
        ///
        /// Note that many current mission systems currently assume equirectangular projection.  For example, from the
        /// PlacesDB user guide v2.0.b.002:
        /// > Orbital images must be in a rectangular projection, with north up in the image. The line (Y) dimension
        /// > of the image is aligned north/south with the sample (X) dimension aligned east/west.
        /// > There is a constant pixel scale (meters/pixel) in both directions. The X and Y scales need not be the
        /// > same, but it is recommended they be.
        ///
        /// The MSL landing site, Gale crater, was 5.4deg S 137.8deg E.  The orbital assets were in equirectangular
        /// projection and did use latitude_of_origin = 0 because the latitude was considered low enough that the
        /// distortion wasn't significant and pixels could be considered square.
        ///
        /// The M2020 landing site, Jezero crater, is 18.38deg N, 77.58deg E.  Orbital assets are still in
        /// equirectangular projection.  Some currently available orbital assets at the time of this writing appear to
        /// still be using latitude_of_origin = 0, though it is planned that during the mission the
        /// latitude_of_projection will be 18.4663.  Thus, using current assets, it may be necessary to account for
        /// horizontal distortion.  But using the final mission assets, again it should be valid to consider pixels
        /// square.
        ///
        /// When using orbital images as DEMS, it may also be necesary to consider fall-off with distance because a
        /// tangent plane to a sphere gets further from the sphere the further away from the tangent point you walk. I
        /// calculate this as on the order of about 16cm at 1km from the tangent point for a planet the size of Mars.
        /// </summary>
        public Vector2 CheckLocalGISImageBasisAndGetResolution(Vector2 originPixel, ILogger logger = null,
                                                               string prefix = null, bool throwOnError = false)
        {
            prefix = prefix ?? "";

            Vector3 bodyNorth = Vector3.Normalize(LonLatToXYZ(new Vector2(0, 90)));

            if (logger != null)
            {
                logger.LogInfo("{0}North (latitude 90) direction in body: ({1:f3}, {2:f3}, {3:f3})",
                               prefix, bodyNorth.X, bodyNorth.Y, bodyNorth.Z);
                
                var meridian = Vector3.Normalize(LonLatToXYZ(new Vector2(0, 0)));
                logger.LogInfo("{0}longitude 0 at equator direction in body: ({1:f3}, {2:f3}, {3:f3})",
                               prefix, meridian.X, meridian.Y, meridian.Z);

                var meridian90 = Vector3.Normalize(LonLatToXYZ(new Vector2(90, 0)));
                logger.LogInfo("{0}longitude 90 at equator direction in body: ({1:f3}, {2:f3}, {3:f3})",
                               prefix, meridian90.X, meridian90.Y, meridian90.Z);

                var xLonLat = XYZToLonLat(new Vector3(1, 0, 0));
                var yLonLat = XYZToLonLat(new Vector3(0, 1, 0));
                var zLonLat = XYZToLonLat(new Vector3(0, 0, 1));
                logger.LogInfo("{0}body frame +X: lat={1:f7}, lon={2:f7}", prefix, xLonLat.Y, xLonLat.X);
                logger.LogInfo("{0}body frame +Y: lat={1:f7}, lon={2:f7}", prefix, yLonLat.Y, yLonLat.X);
                logger.LogInfo("{0}body frame +Z: lat={1:f7}, lon={2:f7}", prefix, zLonLat.Y, zLonLat.X);
            }

            GetLocalGISImageBasisInBodyFrame(originPixel, out Vector3 elevationInBody,
                                             out Vector3 gisImageRightInBody, out Vector3 gisImageDownInBody);

            double elevationScale = elevationInBody.Length();

            if (logger != null && Math.Abs(1 - elevationScale) > 1e-3)
            {
                //I don't really see how this could happen
                logger.LogWarn("GIS elevation scale: {0:f3}", elevationScale);
            }

            var metersPerPixel = new Vector2(gisImageRightInBody.Length(), gisImageDownInBody.Length());

            double pixelAspect = metersPerPixel.X / metersPerPixel.Y;

            if (logger != null)
            {
                logger.LogInfo("{0}GIS local image basis at pixel ({1:f3}, {2:f3})",
                               prefix, originPixel.X, originPixel.Y);
                logger.LogInfo("{0}GIS local meters per pixel: {1:f3}x, {2:f3}y, aspect {3:f6}",
                               prefix, metersPerPixel.X, metersPerPixel.Y, pixelAspect);
            }

            elevationInBody = Vector3.Normalize(elevationInBody);
            gisImageRightInBody = Vector3.Normalize(gisImageRightInBody);
            gisImageDownInBody = Vector3.Normalize(gisImageDownInBody);

            double angle(Vector3 unitVecA, Vector3 unitVecB)
            {
                return Math.Atan2(Vector3.Cross(unitVecA, unitVecB).Length(), Vector3.Dot(unitVecA, unitVecB));
            }

            double rad2deg = 180 / Math.PI;

            double tolDeg = 1e-3;

            double skewAngleDeg = (angle(gisImageRightInBody, gisImageDownInBody) - 0.5 * Math.PI) * rad2deg;
            bool skewOK = skewAngleDeg < tolDeg;

            if (logger != null)
            {
                logger.LogInfo("{0}GIS local image basis skew angle: {1:f3}deg", prefix, skewAngleDeg);
            }

            if (!skewOK)
            {
                string msg = string.Format("{0}GIS local image basis skew angle {1:f3}deg > {2:f3}",
                                           prefix, skewAngleDeg, tolDeg);
                if (logger != null)
                {
                    logger.LogWarn(msg);
                }
                if (throwOnError)
                {
                    throw new Exception(msg);
                }
            }
            
            double rollAngleDeg = (angle(gisImageRightInBody, bodyNorth) - 0.5 * Math.PI) * rad2deg;
            bool rollOK = rollAngleDeg < tolDeg;

            if (logger != null)
            {
                logger.LogInfo("{0}GIS local image basis roll angle: {1:f3}deg", prefix, rollAngleDeg);
            }

            if (!rollOK)
            {
                string msg = string.Format("{0}GIS local image basis roll angle {1:f3}deg > {2:f3}",
                                           prefix, rollAngleDeg, tolDeg);
                if (logger != null)
                {
                    logger.LogWarn(msg);
                }
                if (throwOnError)
                {
                    throw new Exception(msg);
                }
            }

            return metersPerPixel;
        }

        /// <summary>
        /// input: X = column, Y = row, Z = elevation above body radius  
        /// output: X = easting meters, Y = northing meters, Z = elevation above body radius  
        /// easting is distance along standard parallel east from longitude=0
        /// northing is distance above equator along a meridian
        /// </summary>
        public Vector3 ImageToEastingNorthing(Vector3 colRowElev)
        {
            return Vector3.Transform(colRowElev, colRowElevToEastingNorthingElev);
        }

        /// <summary>
        /// input: X = column, Y = row
        /// output: X = easting meters, Y = northing meters
        /// easting is distance along standard parallel east from longitude=0
        /// northing is distance above equator along a meridian
        /// </summary>
        public Vector2 ImageToEastingNorthing(Vector2 colRow)
        {
            return Vector3.Transform(new Vector3(colRow.X, colRow.Y, 0), colRowElevToEastingNorthingElev).XY();
        }

        /// <summary>
        /// input: X = easting meters, Y = northing meters, Z = elevation above body radius  
        /// output: X = column, Y = row, Z = elevation above body radius  
        /// easting is distance along standard parallel east from longitude=0
        /// northing is distance above equator along a meridian
        /// </summary>
        public Vector3 EastingNorthingToImage(Vector3 eastingNorthingElev)
        {
            return Vector3.Transform(eastingNorthingElev, eastingNorthingElevToColRowElev);
        }

        /// <summary>
        /// input: X = easting meters, Y = northing meters
        /// output: X = column, Y = row
        /// easting is distance along standard parallel east from longitude=0
        /// northing is distance above equator along a meridian
        /// </summary>
        public Vector2 EastingNorthingToImage(Vector2 eastingNorthing)
        {
            return EastingNorthingToImage(new Vector3(eastingNorthing.X, eastingNorthing.Y, 0)).XY();
        }

        /// <summary>
        /// input: X = easting meters, Y = northing meters, Z = elevation meters
        /// output: X = longitude degrees, Y = latitude degrees, Z = elevation meters
        /// easting is distance along standard parallel east from longitude=0
        /// northing is distance above equator along a meridian
        /// </summary>
        public Vector3 EastingNorthingToLonLat(Vector3 eastingNorthingElev)
        {
            //return Body.EastingNorthingToLonLat(eastingNorthingElev); //assumes standard parallel = equator
            double[] res = new double[3];
            eastingNorthingElevToLonLatElev
                .TransformPoint(res, eastingNorthingElev.X, eastingNorthingElev.Y, eastingNorthingElev.Z);
            return new Vector3(res[0], res[1], res[2]);
        }

        /// <summary>
        /// input: X = easting meters, Y = northing meters
        /// output: X = longitude degrees, Y = latitude degrees
        /// easting is distance along standard parallel east from longitude=0
        /// northing is distance above equator along a meridian
        /// </summary>
        public Vector2 EastingNorthingToLonLat(Vector2 eastingNorthing)
        {
            return EastingNorthingToLonLat(new Vector3(eastingNorthing.X, eastingNorthing.Y, 0)).XY();
        }

        /// <summary>
        /// input: X = longitude degrees, Y = latitude degrees, Z = elevation meters
        /// output: X = easting meters, Y = northing meters, Z = elevation meters
        /// easting is distance along standard parallel east from longitude=0
        /// northing is distance above equator along a meridian
        /// </summary>
        public Vector3 LonLatToEastingNorthing(Vector3 lonLatElev)
        {
            //return Body.LonLatToEastingNorthing(lonLatElev); //assumes standard parallel = equator
            double[] res = new double[3];
            lonLatElevToEastingNorthingElev.TransformPoint(res, lonLatElev.X, lonLatElev.Y, lonLatElev.Z);
            return new Vector3(res[0], res[1], res[2]);
        }

        /// <summary>
        /// input: X = column, Y = row, Z = elevation above body radius
        /// output: X = longitude degrees, Y = latitude degrees, Z = elevation above body radius
        /// </summary>
        public Vector3 ImageToLonLat(Vector3 colRowElev)
        {
            return EastingNorthingToLonLat(ImageToEastingNorthing(colRowElev));
        }

        /// <summary>
        /// input: X = column, Y = row
        /// output: X = longitude degrees, Y = latitude degrees
        /// </summary>
        public Vector2 ImageToLonLat(Vector2 colRow)
        {
            return ImageToLonLat(new Vector3(colRow.X, colRow.Y, 0)).XY();
        }

        /// <summary>
        /// input: X = longitude degrees, Y = latitude degrees, Z = elevation above body radius
        /// output X = col, Y = row, Z = elevation above body radius
        /// </summary>
        public Vector3 LonLatToImage(Vector3 lonLatElev)
        {
            return EastingNorthingToImage(LonLatToEastingNorthing(lonLatElev));
        }

        /// <summary>
        /// input: X = longitude degrees, Y = latitude degrees
        /// output: X = col, Y = row
        /// </summary>
        public Vector2 LonLatToImage(Vector2 lonLat)
        {
            return LonLatToImage(new Vector3(lonLat.X, lonLat.Y, 0)).XY();
        }

        /// <summary>
        /// Simple minded spherical -> cartesian transform.
        ///
        /// input: X = longitude degres, Y = latitude degrees, Z = elevation above body radius
        /// output: (X, Y, Z) in body frame
        ///
        /// NOTE: See class header comments for definition of planetary body frame. Few if any other mission systems use
        /// this (or any planet-scale) frame, and nominal Landform codepaths should and probably can avoid it as well.
        /// </summary>
        public Vector3 LonLatToXYZ(Vector3 lonLatElev)
        {
            double deg2rad = Math.PI / 180.0;
            double lonRad = lonLatElev.X * deg2rad, latRad = lonLatElev.Y *deg2rad, elevMeters = lonLatElev.Z;
            double radius = Body.Radius + elevMeters;
            double radiusAtLatitude = radius * Math.Cos(latRad); 
            double xInBody = radiusAtLatitude * Math.Cos(lonRad);
            double yInBody = radiusAtLatitude * Math.Sin(lonRad); 
            double zInBody = radius * Math.Sin(latRad); 
            return new Vector3(xInBody, yInBody, zInBody);
        }

        /// <summary>
        /// 2D version of LonLatToXYZ(Vector3) where input elevation is 0
        /// </summary>
        public Vector3 LonLatToXYZ(Vector2 lonLatElev)
        {
            return LonLatToXYZ(new Vector3(lonLatElev.X, lonLatElev.Y, 0));
        }

        /// <summary>
        /// Simple minded cartesian -> spherical transform.
        ///
        /// input: (X, Y, Z) in body frame
        /// output: X = longitude degres, Y = latitude degrees, Z = elevation above body radius
        ///
        /// NOTE: See class header comments for definition of planetary body frame. Few if any other mission systems use
        /// this (or any planet-scale) frame, and nominal Landform codepaths should and probably can avoid it as well.
        /// </summary>
        public Vector3 XYZToLonLat(Vector3 xyzInBody)
        {
            double rad2deg = 180.0 / Math.PI;
            double radius = xyzInBody.Length();
            double elevMeters = radius - Body.Radius;
            double latDeg = Math.Asin(xyzInBody.Z / radius) * rad2deg;
            double lonDeg = Math.Atan2(xyzInBody.Y, xyzInBody.X) * rad2deg;
            return new Vector3(lonDeg, latDeg, elevMeters);
        }

        /// <summary>
        /// input: (X, Y, Z) in body frame
        /// output: X = col, Y = row, Z = elevation above body radius
        ///
        /// NOTE: See class header comments for definition of planetary body frame. Few if any other mission systems use
        /// this (or any planet-scale) frame, and nominal Landform codepaths should and probably can avoid it as well.
        /// </summary>
        public Vector3 XYZToImage(Vector3 xyzInBody)
        {
            return LonLatToImage(XYZToLonLat(xyzInBody));
        }

        /// <summary>
        /// input: X = col, Y = row, Z = elevation above body radius
        /// output: (X, Y, Z) in body frame
        ///
        /// NOTE: See class header comments for definition of planetary body frame. Few if any other mission systems use
        /// this (or any planet-scale) frame, and nominal Landform codepaths should and probably can avoid it as well.
        /// </summary>
        public Vector3 ImageToXYZ(Vector3 colRowElev)
        {
            return LonLatToXYZ(ImageToLonLat(colRowElev));
        }

        /// <summary>
        /// 2D version of ImageToXYZ(Vector3) where input elevation is 0
        /// </summary>
        public Vector3 ImageToXYZ(Vector2 colRow)
        {
            return ImageToXYZ(new Vector3(colRow.X, colRow.Y, 0));
        }

        /// <summary>
        /// Convenience function for measuring the size of an image pixel at the current location.
        ///
        /// assumes each of the corners can have a different sampling rate (arbitrary quadrilateral)
        /// returns the shortest length, the best, finest, smallest number of meters per pixel
        ///
        /// input: (X, Y, Z) in body frame
        /// output: the minimum meters per pixel in the neighborhood of the pixel corresponding to bodyXYZ
        ///
        /// NOTE: See class header comments for definition of planetary body frame. Few if any other mission systems use
        /// this (or any planet-scale) frame, and nominal Landform codepaths should and probably can avoid it as well.
        /// </summary>
        public double GetFinestEstimatedMetersPerPixelAtXYZ(Vector3 bodyXYZ)
        {
            Vector3 pixelColRow = XYZToImage(bodyXYZ);
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
            if (isDisposing && lonLatElevToEastingNorthingElev != null)
            {
                lonLatElevToEastingNorthingElev.Dispose();
                lonLatElevToEastingNorthingElev = null;
            }
            if (isDisposing && eastingNorthingElevToLonLatElev != null)
            {
                eastingNorthingElevToLonLatElev.Dispose();
                eastingNorthingElevToLonLatElev = null;
            }
        }
    }
}
