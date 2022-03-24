using System;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using JPLOPS.Util;
using JPLOPS.Geometry;
using JPLOPS.Imaging;

namespace JPLOPS.Pipeline
{
    public class FaceCount : NodeComponent
    {
        public int NumTris;

        public FaceCount() { }

        public FaceCount(int numTris)
        {
            NumTris = numTris;
        }
    }
}
