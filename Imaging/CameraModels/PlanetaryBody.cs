using System;
using Microsoft.Xna.Framework;
using OSGeo.OSR;
using JPLOPS.MathExtensions;

namespace JPLOPS.Imaging
{
    public abstract class PlanetaryBody
    {
        public abstract string Name { get; }

        public abstract double Radius { get; }

        public abstract SpatialReference MakeSphericalSpatialReference();

        public static PlanetaryBody GetByName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new Exception("planetary body name required");
            }

            switch (name.ToLower())
            {
                case "mars": return new MarsBody();
                case "earth": return new EarthBody();
                default: throw new Exception("unsupported planetary body name: " + name);
            }
        }

        /// <summary>
        /// input: X = easting meters, Y = northing meters, Z = elevation meters
        /// output: X = longitude degrees, Y = latitude degrees, Z = elevation meters
        /// easting is distance along equator east from longitude=0
        /// northing is distance above equator along a meridian
        /// NOTE: do not use this when (easting, northing) is defined relative to an equirectangular projection
        /// with nonzero standard parallel; use GISCameraModel.EastingNorthingToLonLat() instead
        /// </summary>
        public static Vector3 EastingNorthingToLonLat(Vector3 eastingNorthingElev, double bodyRadius)
        {
            //circumference is 2 * PI * radius, so divide by radius to get radians
            double rad2deg = 180 / Math.PI;
            double lon = (eastingNorthingElev.X / bodyRadius) * rad2deg;
            double lat = (eastingNorthingElev.Y / bodyRadius) * rad2deg;
            return new Vector3(lon, lat, eastingNorthingElev.Z);
        }

        /// <summary>
        /// input: X = easting meters, Y = northing meters
        /// output: X = longitude degrees, Y = latitude degrees
        /// easting is distance along equator east from longitude=0
        /// northing is distance above equator along a meridian
        /// NOTE: do not use this when (easting, northing) is defined relative to an equirectangular projection
        /// with nonzero standard parallel; use GISCameraModel.EastingNorthingToLonLat() instead
        /// </summary>
        public static Vector2 EastingNorthingToLonLat(Vector2 eastingNorthing, double bodyRadius)
        {
            return EastingNorthingToLonLat(new Vector3(eastingNorthing.X, eastingNorthing.Y, 0), bodyRadius).XY();
        }

        /// <summary>
        /// input: X = easting meters, Y = northing meters, Z = elevation meters
        /// output: X = longitude degrees, Y = latitude degrees, Z = elevation meters
        /// easting is distance along equator east from longitude=0
        /// northing is distance above equator along a meridian
        /// NOTE: do not use this when (easting, northing) is defined relative to an equirectangular projection
        /// with nonzero standard parallel; use GISCameraModel.EastingNorthingToLonLat() instead
        /// </summary>
        public Vector3 EastingNorthingToLonLat(Vector3 eastingNorthingElev)
        {
            return EastingNorthingToLonLat(eastingNorthingElev, Radius);
        }

        /// <summary>
        /// input: X = easting meters, Y = northing meters
        /// output: X = longitude degrees, Y = latitude degrees
        /// easting is distance along equator east from longitude=0
        /// northing is distance above equator along a meridian
        /// NOTE: do not use this when (easting, northing) is defined relative to an equirectangular projection
        /// with nonzero standard parallel; use GISCameraModel.EastingNorthingToLonLat() instead
        /// </summary>
        public Vector2 EastingNorthingToLonLat(Vector2 eastingNorthing)
        {
            return EastingNorthingToLonLat(eastingNorthing, Radius);
        }

        /// <summary>
        /// input: X = longitude degrees, Y = latitude degrees, Z = elevation meters
        /// output: X = easting meters, Y = northing meters, Z = elevation meters
        /// easting is distance along equator east from longitude=0
        /// northing is distance above equator along a meridian
        /// NOTE: do not use this when (easting, northing) is defined relative to an equirectangular projection
        /// with nonzero standard parallel; use GISCameraModel.LonLatToEastingNorthing() instead
        /// </summary>
        public static Vector3 LonLatToEastingNorthing(Vector3 lonLatElev, double bodyRadius)
        {
            //circumference is 2 * PI * radius, so multiply radians by radius to get circumferential distance
            double deg2rad = Math.PI / 180;
            double easting =  lonLatElev.X * deg2rad * bodyRadius;
            double northing = lonLatElev.Y * deg2rad * bodyRadius;
            return new Vector3(easting, northing, lonLatElev.Z);
        }

        /// <summary>
        /// input: X = longitude degrees, Y = latitude degrees
        /// output: X = easting meters, Y = northing meters
        /// easting is distance along equator east from longitude=0
        /// northing is distance above equator along a meridian
        /// NOTE: do not use this when (easting, northing) is defined relative to an equirectangular projection
        /// with nonzero standard parallel; use GISCameraModel.LonLatToEastingNorthing() instead
        /// </summary>
        public static Vector2 LonLatToEastingNorthing(Vector2 lonLat, double bodyRadius)
        {
            return LonLatToEastingNorthing(new Vector3(lonLat.X, lonLat.Y, 0), bodyRadius).XY();
        }

        /// <summary>
        /// input: X = longitude degrees, Y = latitude degrees, Z = elevation meters
        /// output: X = easting meters, Y = northing meters, Z = elevation meters
        /// easting is distance along equator east from longitude=0
        /// northing is distance above equator along a meridian
        /// NOTE: do not use this when (easting, northing) is defined relative to an equirectangular projection
        /// with nonzero standard parallel; use GISCameraModel.LonLatToEastingNorthing() instead
        /// </summary>
        public Vector3 LonLatToEastingNorthing(Vector3 lonLatElev)
        {
            return LonLatToEastingNorthing(lonLatElev, Radius);
        }

        /// <summary>
        /// input: X = longitude degrees, Y = latitude degrees
        /// output: X = easting meters, Y = northing meters
        /// easting is distance along equator east from longitude=0
        /// northing is distance above equator along a meridian
        /// NOTE: do not use this when (easting, northing) is defined relative to an equirectangular projection
        /// with nonzero standard parallel; use GISCameraModel.LonLatToEastingNorthing() instead
        /// </summary>
        public Vector2 LonLatToEastingNorthing(Vector2 lonLat)
        {
            return LonLatToEastingNorthing(lonLat, Radius);
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
            //TODO should this maybe use an ellipsoid or geoid?
            //great care should be taken in attempting such a feat
            //currently there are assumptions in other places
            //particularly LonLatToXYZ() and XYZToLonLat(), that assume a simple sphere
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
            //TODO should this maybe use an ellipsoid or geoid?
            //great care should be taken in attempting such a feat
            //currently there are assumptions in other places
            //particularly LonLatToXYZ() and XYZToLonLat(), that assume a simple sphere
            ret.SetGeogCS("Earth CRS", "World Geodetic System 1984", "Earth",
                          Osr.SRS_WGS84_SEMIMAJOR, 0 /* Osr.SRS_WGS84_INVFLATTENING */,
                          "Greenwich", 0.0, "Degree", Math.PI / 180.0);
            return ret;
        }
    }
}
