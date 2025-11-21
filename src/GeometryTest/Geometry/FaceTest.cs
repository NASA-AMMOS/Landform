using Xunit;
using System.Collections.Generic;
using JPLOPS.Geometry;

namespace GeometryTest
{
    public class FaceTest
    {
        [Fact]
        public void BasicFaceMethodTest()
        {
            Face f = new Face(1, 2, 4);
            Assert.Equal(1, f.P0);
            Assert.Equal(2, f.P1);
            Assert.Equal(4, f.P2);

            Face z = new Face(1, 2, 4);
            Dictionary<Face, int> dict = new Dictionary<Face, int>();
            dict.Add(f, 1);
            Assert.True(dict.ContainsKey(z));

            int[] a = new int[3];
            f.FillArray(a);
            Assert.Equal(1, a[0]);
            Assert.Equal(2, a[1]);
            Assert.Equal(4, a[2]);


            int[] b = f.ToArray();
            Assert.Equal(1, b[0]);
            Assert.Equal(2, b[1]);
            Assert.Equal(4, b[2]);


            Face j = new Face(new int[] { 5, 6, 7 });
            Assert.Equal(5, j.P0);
            Assert.Equal(6, j.P1);
            Assert.Equal(7, j.P2);

            Face c = new Face(j);
            Assert.Equal(5, c.P0);
            Assert.Equal(6, c.P1);
            Assert.Equal(7, c.P2);
        }

        [Fact]
        public void FaceIsValidTest()
        {
            Assert.True(new Face(1, 2, 4).IsValid());
            Assert.False(new Face(1, 1, 4).IsValid());
            Assert.False(new Face(1, 3, 1).IsValid());
            Assert.False(new Face(0, 1, 1).IsValid());
        }        
    }
}
