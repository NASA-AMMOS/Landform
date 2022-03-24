using System.Text;

namespace JPLOPS.Geometry
{
    /// <summary>
    /// Represents the desired attributes of an output mesh from MeshLab
    /// </summary>
    class MeshLabOutputAttributes
    {
        bool hasNormals;
        bool hasUvs;
        bool hasColors;

        public MeshLabOutputAttributes(Mesh m)
        {
            Init(m.HasNormals, m.HasUVs, m.HasColors);
        }

        public MeshLabOutputAttributes(bool normals, bool uvs, bool colors)
        {
            Init(normals, uvs, colors);
        }

        void Init(bool normals, bool uvs, bool colors)
        {
            this.hasNormals = normals;
            this.hasUvs = uvs;
            this.hasColors = colors;           
        }

        public bool Matches(Mesh mesh)
        {
            return this.hasNormals == mesh.HasNormals && this.hasUvs == mesh.HasUVs && this.hasColors == mesh.HasColors;
        }
          
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            if (hasNormals || hasUvs || hasColors)
            {
                sb.Append(" -m ");
            }
            if (hasNormals)
            {
                sb.Append("vn ");
            }
            if (hasColors)
            {
                sb.Append("vc ");
            }
            if (hasUvs)
            {
                sb.Append("vt wt ");
            }
            return sb.ToString();
        }

    }

}
