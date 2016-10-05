using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OPS.Geometry
{
    public class Triangle
    {
        public Vertex V0;
        public Vertex V1;
        public Vertex V2;

        public Triangle()
        {

        }

        public Triangle(Vertex v0, Vertex v1, Vertex v2)
        {
            this.V0 = v0;
            this.V1 = v1;
            this.V2 = v2;
        }
    }
}