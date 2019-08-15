using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using OSGeo.OSR;

namespace OPS.Imaging
{
    class MarsSpatialTranslator : ISpatialTranslator
    {
        /// <summary>
        /// Radius of Mars
        /// </summary>
        const double RADIUS = 3396190.0;

        /// <summary>
        /// Spherical spatial reference system for Mars, using degrees as
        /// unit of measure.
        /// </summary>
        static SpatialReference SphericalSpatialReference;

        static MarsSpatialTranslator()
        {
            SphericalSpatialReference = new SpatialReference(null);
            SphericalSpatialReference.SetGeogCS("Mars Spherical",
                    "SPHERICAL_MARS", "Mars", RADIUS, 0,
                    "Marsridian", 0.0, "Degree", Math.PI / 180.0);
        }

        public Vector3 ProjectedToWorld(Vector3 projected)
        {
            throw new NotImplementedException();
        }

        public Vector3 WorldToProjected(Vector3 world)
        {
            throw new NotImplementedException();
        }
    }
}
