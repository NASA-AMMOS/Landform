using System;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using OPS.Util;
using OPS.Geometry;
using OPS.Imaging;

namespace OPS.Pipeline
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
