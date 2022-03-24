using System;

namespace JPLOPS.Geometry
{
    /// <summary>
    /// Serializer for saving meshes as collada (dae) format
    /// Currently uses meshlab to convert the mesh but can be modified
    /// in the future to use assimp to avoid the dependency on meshlab
    /// Reading is not supported
    /// </summary>
    public class DAESerializer : MeshSerializer
    {
        public override string GetExtension()
        {
            return ".dae";
        }

        public override Mesh Load(string filename)
        {
            throw new NotImplementedException();
        }

        public override void Save(Mesh m, string filename, string imageFilename)
        {
            MeshLab.SaveAs(filename, m, imageFilename);
        }
    }
}
