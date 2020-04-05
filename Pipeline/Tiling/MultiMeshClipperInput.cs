using OPS.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Imaging;

namespace OPS.Pipeline
{
    /// <summary>
    /// Class for specifing input datasets to MultiMeshClipper
    /// </summary>
    public class MultiMeshClipperInput
    {
        public Mesh Mesh;
        public Image Image;
        public Image Index;
        public MeshOperator MeshOperator;

        public MultiMeshClipperInput(string meshFilename, string imageFilename)
        {
            Init(Mesh.Load(meshFilename), imageFilename == null ? null : Image.Load(imageFilename));
        }

        public MultiMeshClipperInput(Mesh mesh, Image img)
        {
            Init(mesh, img);
        }

        void Init(Mesh m, Image img)
        {
            this.Mesh = m;
            if (!this.Mesh.HasNormals)
            {
                this.Mesh.GenerateVertexNormals();
            }
            this.MeshOperator = new MeshOperator(this.Mesh, buildUVFaceTree: false, buildVertexTree: !this.Mesh.HasFaces);
            this.Image = img;
        }
    }
}
