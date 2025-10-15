using Microsoft.Xna.Framework;
using JPLOPS.Pipeline;
using JPLOPS.Geometry;

namespace PipelineTest
{
    public class RoverCoordinateSystemTest
    {
        [Fact]
        public void TestLocalLevelUnityConversion()
        {
            Vector3 localLevel = new Vector3(1, 2, 3);
            Vector3 unity = Vector3.Transform(localLevel, RoverCoordinateSystem.LocalLevelToUnity);
            Assert.Equal(new Vector3(2,-3,1), unity);
            Assert.Equal(localLevel, Vector3.Transform(unity, RoverCoordinateSystem.UnityToLocalLevel));

            Mesh m = new Mesh();
            m.Vertices.Add(new Vertex(localLevel));
            RoverCoordinateSystem.LocalLevelToUnityMesh(m);
            Assert.Equal(unity, m.Vertices[0].Position);
            RoverCoordinateSystem.UnityToLocalLevelMesh(m);
            Assert.Equal(localLevel, m.Vertices[0].Position);
        }
    }
}
