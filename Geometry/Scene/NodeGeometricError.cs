using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Geometry
{
    public class NodeGeometricError : NodeComponent
    {
        public double Error;

        public NodeGeometricError() { }

        public NodeGeometricError(double error) { this.Error = error; }
    }
}
