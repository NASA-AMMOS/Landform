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
    public interface DEMBody
    {
        double GetRadius();
        SpatialReference MakeSphericalSpatialReference();
    }

    /// <summary>
    /// ported from TerrainTools/ImageLib/Pipeline/Mars.cs
    /// </summary>
    public class MarsBody : DEMBody
    {
        public double GetRadius()
        {
            return 3396190.0;
        }

        public SpatialReference MakeSphericalSpatialReference()
        {
            var ret = new SpatialReference(null);
            ret.SetGeogCS("Mars Spherical", "SPHERICAL_MARS", "Mars",
                          GetRadius(), 0, "Marsridian", 0.0, "Degree", Math.PI / 180.0);
            return ret;
        }
    }

    //https://gdal.org/tutorials/osr_api_tut.html#defining-a-geographic-coordinate-reference-system
    public class EarthBody : DEMBody
    {
        public double GetRadius()
        {
            return Osr.SRS_WGS84_SEMIMAJOR;
        }

        public SpatialReference MakeSphericalSpatialReference()
        {
            var ret = new SpatialReference(null);
            ret.SetGeogCS("Earth CRS", "World Geodetic System 1984", "Earth",
                          Osr.SRS_WGS84_SEMIMAJOR, Osr.SRS_WGS84_INVFLATTENING,
                          "Greenwich", 0.0, "Degree", Math.PI / 180.0);
            return ret;
        }
    }
}