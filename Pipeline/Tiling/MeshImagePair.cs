using JPLOPS.Imaging;
using JPLOPS.Geometry;

namespace JPLOPS.Pipeline
{
    /// <summary>
    /// Content container for adding mesh and image data to a scene node
    /// </summary>
    public class MeshImagePair : NodeComponent
    {
        public Mesh Mesh;
        public Image Image;
        public Image Index;
        public MeshOperator MeshOp;

        public MeshImagePair() { }

        public MeshImagePair(Mesh mesh = null, Image image = null, Image index = null, MeshOperator meshOp = null)
        {
            this.Mesh = mesh;
            this.Image = image;
            this.Index = index;
            this.MeshOp = meshOp;
        }

        public MeshOperator EnsureMeshOperator(bool buildUVFaceTree = false)
        {
            if (MeshOp == null || (buildUVFaceTree && !MeshOp.HasUVFaceTree))
            {
                MeshOp = new MeshOperator(Mesh, buildFaceTree: Mesh.HasFaces, buildUVFaceTree: buildUVFaceTree,
                                          buildVertexTree: !Mesh.HasFaces);
            }
            return MeshOp;
        }
    }
}
