using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Assimp;


namespace OPS.AssimpInterface
{
    public class AssimpSerializer
    {

        public void Export(Geometry.Mesh geoMesh, string meshFilepath)
        {
            Export(geoMesh, null, meshFilepath);
        }

        public void Export(Geometry.Mesh geoMesh, string textureFilepath, string meshFilepath)
        {                        
            Mesh mesh = new Mesh((geoMesh.Faces.Count == 0) ? PrimitiveType.Point : PrimitiveType.Triangle);
            if (geoMesh.HasUVs)
            {                
                mesh.TextureCoordinateChannels[0] = new List<Vector3D>();
            }
            
            if(geoMesh.HasColors)
            {
                mesh.VertexColorChannels[0] = new List<Color4D>();
            }
            foreach (var geoVert in geoMesh.Vertices)
            {
                mesh.Vertices.Add(new Vector3D((float)geoVert.Position.X, (float)geoVert.Position.Y, (float)geoVert.Position.Z));
                if(geoMesh.HasNormals)
                {
                    mesh.Normals.Add(new Vector3D((float)geoVert.Normal.X, (float)geoVert.Normal.Y, (float)geoVert.Normal.Z));
                }
                if (geoMesh.HasUVs)
                {                    
                    mesh.TextureCoordinateChannels[0].Add(new Vector3D((float)geoVert.UV.U, (float)geoVert.UV.V, 0));
                }
                if(geoMesh.HasColors)
                {
                    mesh.VertexColorChannels[0].Add(new Color4D((float)geoVert.Color.R, (float)geoVert.Color.G, (float)geoVert.Color.B, (float)geoVert.Color.A));
                }
            }
            foreach (var f in geoMesh.Faces)
            {
                mesh.Faces.Add(new Face(new int[] { f.P0, f.P1, f.P2 }));
            }
            Scene s = new Scene();
            Material mat = new Material();
            mat.ColorDiffuse = new Color4D(1, 1, 1, 1);
            if (textureFilepath != null)
            {
                // TODO: Add texture
            }
            s.Materials.Add(mat);
            s.Meshes.Add(mesh);
            
            AssimpContext context = new AssimpContext();
            //context.RemoveConfigs();
            //context.SetConfig(new Assimp.Configs.ASEReconstructNormalsConfig(false));
            //context.RemoveConfig("NormalSmoothingAngleConfig");
            string formatId = null;
            string targetExtension = Path.GetExtension(meshFilepath).ToLower();
            foreach (var formatDescription in context.GetSupportedExportFormats())
            {
                if (targetExtension.Equals("." + formatDescription.FileExtension.ToLower()))
                {
                    formatId = formatDescription.FormatId;
                }
            }
            if(formatId == null)
            {
                throw new Exception("Input format not supported " + targetExtension);
            }    
            s.RootNode = new Node(Path.GetFileName(meshFilepath));
            s.RootNode.Transform = Matrix4x4.Identity;
            s.RootNode.MeshIndices.Add(0);
            context.ExportFile(s, meshFilepath, formatId);
        }

        public Geometry.Mesh Import(string filepath)
        {
            AssimpContext importer = new AssimpContext();
            Scene s = importer.ImportFile(filepath);

            if(s.MeshCount != 1)
            {
                throw new Exception("Unsupported number of meshes in file.  Expected 1 found " + s.MeshCount);
            }
            var m = s.Meshes[0];
            Geometry.Mesh geoMesh = new Geometry.Mesh();
            geoMesh.HasColors = m.HasVertexColors(0);
            geoMesh.HasNormals = m.HasNormals;
            geoMesh.HasUVs = m.HasTextureCoords(0);
            
            for(int i = 0; i < m.Vertices.Count; i++)
            {                
                var vertex = new Geometry.Vertex();
                var v = m.Vertices[i];
                vertex.Position = new Microsoft.Xna.Framework.Vector3(v.X, v.Y, v.Z);
                if(geoMesh.HasColors)
                {
                    var c = m.VertexColorChannels[0][i];
                    vertex.Color = new Microsoft.Xna.Framework.Vector4(c.R, c.G, c.B, c.A);
                }
                if(geoMesh.HasNormals)
                {
                    var n = m.Normals[i];
                    vertex.Normal = new Microsoft.Xna.Framework.Vector3(n.X, n.Y, n.Z);
                }
                if (geoMesh.HasUVs)
                {
                    var uv = m.TextureCoordinateChannels[0][i];
                    vertex.UV = new Microsoft.Xna.Framework.Vector2(uv.X, uv.Y);
                }
                geoMesh.Vertices.Add(vertex);
            }
            foreach(var f in m.Faces)
            {
                geoMesh.Faces.Add(new Geometry.Face(f.Indices[0], f.Indices[1], f.Indices[2]));
            }
            return geoMesh;
        }
    }
}
