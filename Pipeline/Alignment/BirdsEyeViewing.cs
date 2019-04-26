using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using Microsoft.Xna.Framework;

namespace OPS.Pipeline
{
    public class BirdsEyeViewing
    {
        public enum ColorMode { Texture, Tilt, Elevation };
    }

    public class SpatialMatch
    {
        public Vector3 ModelPoint;
        public Vector3 DataPoint;

        public SpatialMatch(Vector3 modelPoint, Vector3 dataPoint)
        {
            ModelPoint = modelPoint;
            DataPoint = dataPoint;
        }
    }
}
