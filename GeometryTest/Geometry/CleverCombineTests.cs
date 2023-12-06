using Microsoft.VisualStudio.TestTools.UnitTesting;
using JPLOPS.Geometry;
using System.Linq;
using Microsoft.Xna.Framework;

namespace GeometryTest
{
    [TestClass()]
    public class CleverCombineTests
    {
        [TestMethod()]
        public void CombineTest()
        {
            Vector3[] origins = new Vector3[] { new Vector3(1, 2, 3), new Vector3(50, 60, 70) };

            Mesh pointCloud0 = new Mesh();
            pointCloud0.Vertices.Add(new Vertex(new Vector3(1.1, 2.1, 3.1)));
            pointCloud0.Vertices.Add(new Vertex(new Vector3(4.1, 5.1, 6.1)));
            pointCloud0.Vertices.Add(new Vertex(new Vector3(7.1, 8.1, 9.1)));

            Mesh pointCloud1 = new Mesh();
            pointCloud1.Vertices.Add(new Vertex(new Vector3(1.099, 2.099, 3.099)));
            pointCloud1.Vertices.Add(new Vertex(new Vector3(4.099, 5.099, 6.099)));
            pointCloud1.Vertices.Add(new Vertex(new Vector3(7.099, 8.099, 9.099)));

            Mesh result = (new CleverCombine()).Combine(new Mesh[] { pointCloud0, pointCloud1 }, origins);
            Assert.IsTrue(result.Vertices.Count == 3);

            var p0Positions = pointCloud0.Vertices.Select(v => v.Position);
            foreach(var pos in result.Vertices.Select(v => v.Position))
            {
                Assert.IsTrue(p0Positions.Contains(pos));
            }
        }
    }
}
