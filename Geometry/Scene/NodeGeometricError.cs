namespace JPLOPS.Geometry
{
    public class NodeGeometricError : NodeComponent
    {
        public double Error;

        public NodeGeometricError() { }

        public NodeGeometricError(double error) { this.Error = error; }
    }
}
