using System;
using System.Collections.Generic;
using JPLOPS.Geometry;
using JPLOPS.Util;

namespace GeometryThirdpartyTest
{
    public class FSSRTest
    {
        [Fact]
        public void FSSRReconstruct()
        {
            Mesh m = TestMeshCreator.CreateMesh(true, false, false);
            m.Faces = new List<Face>();
            Mesh r = FSSR.Reconstruct(m);
            r.Save("fssr_recon.ply");
            Assert.NotEmpty(r.Vertices);
            Assert.NotEmpty(r.Faces);
            Assert.True(m.HasNormals);
            Assert.False(m.HasUVs);
            Assert.False(m.HasColors);
        }
    }
}
