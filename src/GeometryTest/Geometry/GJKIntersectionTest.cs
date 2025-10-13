using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Xna.Framework;
using JPLOPS.Geometry;
using System;
using System.Collections.Generic;

namespace GeometryTest
{
    [TestClass]
    public class GJKIntersectionTest
    {
        [TestMethod]
        public void GJKIntersectionCubes()
        {
            List<Vector3> unitCubeVerts = new List<Vector3>();
            for (int i = 0; i < 8; i++)
            {
                bool x = (i & 1) != 0;
                bool y = (i & 2) != 0;
                bool z = (i & 4) != 0;
                unitCubeVerts.Add(new Vector3(
                    x ? 0.5 : -0.5,
                    y ? 0.5 : -0.5,
                    z ? 0.5 : -0.5
                    ));
            }
            ConvexHull unitCube = ConvexHull.Create(unitCubeVerts);

            Func<Vector3, Vector3, ConvexHull> CubeAt = (center, size) =>
              {
                  Matrix m = Matrix.CreateScale(size) * Matrix.CreateTranslation(center);
                  return ConvexHull.Transformed(unitCube, m);
              };

            var leftCube = CubeAt(new Vector3(-3, 0, 0), new Vector3(1, 1, 1));
            var rightCube = CubeAt(new Vector3(3, 0, 0), new Vector3(1, 1, 1));
            Assert.AreEqual(false, leftCube.Intersects(rightCube));

            var bigCube = CubeAt(new Vector3(0, 0, 0), new Vector3(8, 8, 8));
            Assert.AreEqual(true, bigCube.Intersects(leftCube));
            Assert.AreEqual(true, bigCube.Intersects(rightCube));

            var tallCube = CubeAt(new Vector3(0, 0, 0), new Vector3(2, 5, 1));
            Assert.AreEqual(false, tallCube.Intersects(leftCube));
            Assert.AreEqual(false, tallCube.Intersects(rightCube));
            Assert.AreEqual(true, tallCube.Intersects(bigCube));
        }
    }
}
