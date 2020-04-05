using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using OSGeo.GDAL;
using OSGeo.OSR;

namespace OPS.Imaging
{
    public class GISCameraModel : CameraModel
    {
        readonly public int Width;
        readonly public int Height;

        protected PlanetaryBody Body { get; private set; }

        private SpatialReference bodyFrame, projectedFrame;
        private CoordinateTransformation latLonToProjected, projectedToLatLon;

        private Matrix geoTransform;
        private Matrix invGeoTransform;

        private Matrix MeshToOrbitalBody;
        protected GISCameraModel() { }

        public GISCameraModel(string file, string demBodyType, Matrix meshToOrbitalBody)
        {
            using (Dataset Dataset = Gdal.Open(file, Access.GA_ReadOnly))
            {
                this.Body = PlanetaryBody.GetByName(demBodyType);

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

                bodyFrame = Body.MakeSphericalSpatialReference();
                projectedFrame = new SpatialReference(Dataset.GetProjectionRef());

                latLonToProjected = new CoordinateTransformation(bodyFrame, projectedFrame);
                projectedToLatLon = new CoordinateTransformation(projectedFrame, bodyFrame);

                Width = Dataset.RasterXSize;
                Height = Dataset.RasterYSize;

                MeshToOrbitalBody = meshToOrbitalBody;
            }
        }

        public GISCameraModel(GISCameraModel that)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Fast reference based method projecting a ray.  Camera model classes
        /// implement this method
        /// </summary>
        /// <param name="pixelPos"></param>
        /// <param name="ray"></param>
        /// <returns></returns>
        public override void Unproject(ref Vector2 pixelPos, out Ray ray)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Project a 3D position to a pixel location in an image
        /// </summary>
        /// <param name="pos"></param>
        /// <returns>col, row</returns>
        public override Vector2 Project(Vector3 pos, out double range)
        {
            range = 0; //NOT SUPPORTED 

            var ptBodyXYZ = Vector3.Transform(pos, MeshToOrbitalBody);

            var lonlat = XYZToLatLon(ptBodyXYZ); 
            var pixel = LatLonToImage(lonlat); 
            return new Vector2(pixel.X, pixel.Y); 
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
        /// XYZ is sphere in a left-handed axis convention with x to north pole; y though lon 90, lat 0 (equator); z through 0 lon, 0 lat (equator);
        /// return X = longitude, Y = latitude
        /// </summary>
        protected Vector3 XYZToLatLon(Vector3 worldPos, double lat0 = 0, double lon0 = 0)
        {
            double r = worldPos.Length();
            double lat = Math.Asin(worldPos.X / r);
            double lon = Math.Atan2(worldPos.Y, worldPos.Z);
            return new Vector3(lon * 180 / Math.PI + lon0, lat * 180 / Math.PI + lat0, r - Body.Radius);
        }

        public override object Clone()
        {
            return new GISCameraModel(this);
        }

        public override bool Linear
        {
            get
            {
                return false;
            }
        }

        /// <summary>
        /// the direction normal to the image plane and pointing outward.
        /// This is not necessarily the direction through the middle pixel of your image.
        /// </summary>
        public override Vector3 ImagePlaneNormal
        {
            get
            {
                return new Vector3(0, 0, 1); //ISSUE #1039: figure out the best way to handle this approximation
            }
        }
    }
}
