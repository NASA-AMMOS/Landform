using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Assimp;


namespace OPS.Experimental.AssimpInterface
{
    /// <summary>
    /// Mesh import and export routines using Assimp library
    /// Assimp has a tendency to modify mesh structure on import and export
    /// so use with caution.  The class is meant for import an export of simple
    /// mesh structures and may not work correctly for all mesh and file types.
    /// </summary>
    public class AssimpSerializer
    {
        /// <summary>
        /// Basic exporter using assimp library
        /// This exporter can write dae and stl files 
        /// </summary>
        /// <param name="geoMesh"></param>
        /// <param name="textureFilepath"></param>
        /// <param name="meshFilepath"></param>
        public static void Export(Geometry.Mesh geoMesh, string meshFilepath, string textureFilepath = null)
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
                TextureSlot ts = new TextureSlot(textureFilepath, TextureType.Diffuse, 0, TextureMapping.FromUV, 0, 1, TextureOperation.Add, TextureWrapMode.Clamp, TextureWrapMode.Clamp, 0);
                mat.TextureDiffuse = ts;
            }
            s.Materials.Add(mat);
            s.Meshes.Add(mesh);
            
            AssimpContext context = new AssimpContext();
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

        /// <summary>
        /// Basic importer for mesh filetypes supported by assimp.  
        /// This importer is not complete and may not work with all filetypes.
        /// Use with caution
        /// </summary>
        /// <param name="filepath"></param>
        /// <returns></returns>
        public static Geometry.Mesh Import(string filepath)
        {
            AssimpContext importer = new AssimpContext();
            Scene s = importer.ImportFile(filepath);

            if(s.MeshCount != 1)
            {
                throw new Geometry.MeshSerializerException("Unsupported number of meshes in file.  Expected 1 found " + s.MeshCount);
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
                if (f.IndexCount == 1)
                {
                    // This is a single point.  Mesh is probably a point cloud.  Do not create a face for it.
                    continue;
                }
                else if (f.IndexCount == 3)
                {
                    geoMesh.Faces.Add(new Geometry.Face(f.Indices[0], f.Indices[1], f.Indices[2]));
                }
                else
                {
                    throw new Exception("Face has an unsupported number of indices: " + f.IndexCount);
                }
            }
            geoMesh.RemoveDuplicateVertices();
            return geoMesh;
        }
    }
}
