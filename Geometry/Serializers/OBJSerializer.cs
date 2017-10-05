using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Geometry
{

    public class OBJSerializerException : MeshSerializerException {
        public OBJSerializerException() {}
        public OBJSerializerException(string message) : base(message) {}
        public OBJSerializerException(string message, Exception inner) : base(message, inner) {}
    }

    /// <summary>
    /// Class for reading and writing objs.  Objs will
    /// be made one-to-one in the read process so that
    /// they can be stored in our standard Mesh structure
    /// </summary>
    public class OBJSerializer : MeshSerializer
    {

        /// <summary>
        /// Defines the position, uv, and normal for a vertex
        /// by specifying the index into each of the respective arrays
        /// OBJs support mutliple indices so that positions, uvs, and normals
        /// can be reused on a vert by vert basis
        /// </summary>
        private struct VertexDefinition
        {
            public int vertIdx;
            public int uvIdx;
            public int normalIdx;

            public VertexDefinition(int v, int uv, int n)
            {
                this.vertIdx = v;
                this.uvIdx = uv;
                this.normalIdx = n;
            }

            public override int GetHashCode()
            {
                int hash = 17;
                hash = hash * 23 + vertIdx.GetHashCode();
                hash = hash * 23 + uvIdx.GetHashCode();
                hash = hash * 23 + normalIdx.GetHashCode();
                return hash;
            }
        }

        /// <summary>
        /// Structure to reperesent three Vertex Definitions in an obj file that make up a face
        /// </summary>
        private class OBJFace
        {
            public VertexDefinition[] vertDef;
            public OBJFace()
            {
                vertDef = new VertexDefinition[3];
            }
        }

        public static Mesh Read(string filename, double defaultAlpha = 1, int capacity=100)
        {
            using (StreamReader sr = new StreamReader(filename))
            {
                return Read(sr, defaultAlpha, capacity);
            }
        }

        /// <summary>
        /// Reads an obj mesh from a file.  
        /// This method supports two types of obj meshes
        /// 1) obj meshes with one or more faces defined.  In this case it will disregard any
        /// position, uv, or normal elements not referenced by at least one face.  It will create a 
        /// vertex structure for each unique combination of position, uv, and normal in the file and
        /// assing face indices accordingly.
        /// 2) obj meshes that contain no faces but have a number of uv and normal elements equal to
        /// 0 or the number of position elements.  In this case the obj is treated as a point cloud and a 
        /// one-to-one correspondence is assumed between position, uv, and normal index.
        /// 
        /// Order of the position, uv, and normal attributes is not maintained in the resulting mesh
        /// </summary>
        /// <param name="filename">Filename of the mesh to read</param>
        /// <param name="defaultAlpha">OBJ doesn't support vertex colors but some tools write the RGB component anyway.  Use this value to set the alpha</param>
        /// <param name="capacity">Optional starting capacity for mesh data structure</param>
        /// <returns></returns>
        public static Mesh Read(StreamReader sr, double defaultAlpha = 1, int capacity = 100)
        {
            // OBJs can contain different length arrays of vert, uv, normals.
            // Thus each face indices each of these attributes individually.
            // We use lists to temporarily store the vert, uv, normals, and 
            // faces in the more complicated structure and then convert them 
            // into a one-2-one indexing scheme where all attribute arrays are 
            // the same length.           

            // Read raw file data into arrays
            List<Vector3> vertices = new List<Vector3>(capacity);
            List<Vector4> colors = new List<Vector4>(capacity);
            List<Vector2> uvs = new List<Vector2>(capacity);
            List<Vector3> normals = new List<Vector3>(capacity);
            List<OBJFace> objFaces = new List<OBJFace>(capacity);
            String line = sr.ReadLine();
            while (line != null)
            {
                string[] parts = line.Split().Where(s => s.Length != 0).ToArray();
                if (line.StartsWith("v "))
                {
                    vertices.Add(new Vector3(double.Parse(parts[1]), double.Parse(parts[2]), double.Parse(parts[3])));
                    // obj doesn't offically support vertex colors but some tools pack them after the xyz component in 
                    if (parts.Length >= 7)
                    {
                        colors.Add(new Vector4(double.Parse(parts[4]), double.Parse(parts[5]), double.Parse(parts[6]), defaultAlpha));
                    }
                }
                else if (line.StartsWith("vt"))
                {
                    uvs.Add(new Vector2(double.Parse(parts[1]), double.Parse(parts[2])));
                }
                else if (line.StartsWith("vn"))
                {
                    normals.Add(new Vector3(double.Parse(parts[1]), double.Parse(parts[2]), double.Parse(parts[3])));
                }
                else if (line.StartsWith("f"))
                {
                    OBJFace f = new OBJFace();
                    for (int i = 1; i < 4; i++)
                    {
                        string[] pointParts = parts[i].Split('/');
                        if (pointParts.Length == 1)
                        {
                            f.vertDef[i - 1].vertIdx = int.Parse(pointParts[0]) - 1;
                        }
                        else if (pointParts.Length == 2)
                        {
                            f.vertDef[i - 1].vertIdx = int.Parse(pointParts[0]) - 1;
                            f.vertDef[i - 1].uvIdx = int.Parse(pointParts[1]) - 1;
                        }
                        else if (pointParts.Length == 3)
                        {
                            f.vertDef[i - 1].vertIdx = int.Parse(pointParts[0]) - 1;
                            if (pointParts[1].Length > 0)
                            {
                                f.vertDef[i - 1].uvIdx = int.Parse(pointParts[1]) - 1;
                            }
                            if (pointParts[2].Length > 0)
                            {
                                f.vertDef[i - 1].normalIdx = int.Parse(pointParts[2]) - 1;
                            }
                        }
                    }
                    objFaces.Add(f);
                }
                line = sr.ReadLine();
            }

            // Generate a mesh
            Mesh result = new Mesh();
            result.HasNormals = normals.Count != 0;
            result.HasUVs = uvs.Count != 0;
            result.HasColors = colors.Count != 0;
            if (result.HasColors && vertices.Count != colors.Count)
            {
                throw new OBJSerializerException("Not all vertices in OBJ defined colors.  If any vertex defines a color then they all must");
            }
            if (objFaces.Count == 0)
            {
                // This is a weird OBJ file which doesn't define any faces.  The spec is unclear on how to interpret the relationship between vertices and
                // uvs/normals in this case.  We make the assumption that in the absence of faces, the uv and normal elements have a one-to-one mapping with
                // vertices.  This, if either list is defined it must also be the same lenght as the list of vertices.
                // This assumption allows us to read in obj point clouds.
                if (result.HasUVs && uvs.Count != vertices.Count)
                {
                    throw new OBJSerializerException("OBJ did not contain face discription and number of vertices and uvs differs");
                }
                if (result.HasNormals && normals.Count != vertices.Count)
                {
                    throw new OBJSerializerException("OBJ did not contain face discription and number of vertices and uvs differs");
                }
                for (int i = 0; i < vertices.Count; i++)
                {
                    Vertex v = new Vertex();
                    v.Position = vertices[i];
                    v.UV = result.HasUVs ? uvs[i] : Vector2.Zero;
                    v.Normal = result.HasNormals ? normals[i] : Vector3.Zero;
                    v.Color = result.HasColors ? colors[i] : Vector4.Zero;
                    result.Vertices.Add(v);
                }
            }
            else
            {
                // This is a normal obj file.  Generate a mesh using the faces.  Any vertices or uvs not referenced by a face will be ommitted.
                Dictionary<VertexDefinition, int> vertDefToIndex = new Dictionary<VertexDefinition, int>();
                foreach (OBJFace f in objFaces)
                {
                    int[] indices = new int[3];
                    // Construct a vertex object for each of the vertices defined by the face
                    for (int i = 0; i < 3; i++)
                    {
                        VertexDefinition vertDef = f.vertDef[i];
                        // If we haven't seen a vertex like this before, create a new one
                        if (!vertDefToIndex.ContainsKey(vertDef))
                        {
                            Vertex v = new Vertex();
                            v = new Vertex();
                            v.Position = vertices[vertDef.vertIdx];
                            v.Color = result.HasColors ? colors[vertDef.vertIdx] : Vector4.Zero;
                            v.UV = result.HasUVs ? uvs[vertDef.uvIdx] : Vector2.Zero;
                            v.Normal = result.HasNormals ? normals[vertDef.normalIdx] : Vector3.Zero;
                            vertDefToIndex.Add(vertDef, vertDefToIndex.Count);
                            result.Vertices.Add(v);
                        }
                        indices[i] = vertDefToIndex[vertDef];
                    }
                    // Create a face from our vertex indices
                    result.Faces.Add(new Face(indices));
                }
            }
            return result;
        }

        /// <summary>
        /// Saves a mesh out as an obj file.  Note that obj format does not offically support
        /// color vertex attributes so these will be lost.  Only position, uv, and normals will be 
        /// written out.  If the optional textureFilename is included a .mtl file will be created with the
        /// same name as the mesh specifying to use the supplied image as a diffuse texture.
        /// </summary>
        /// <param name="mesh">Mesh to export</param>
        /// <param name="filename">Output filename</param>
        /// <param name="textureFilename">Optional diffuse texture to include as a material</param>
        public static void Write(Mesh mesh, string filename, string textureFilename = null, bool writeColors = true)
        {
            string mtlFilename = Path.Combine(Path.GetDirectoryName(filename), Path.GetFileNameWithoutExtension(filename)) + ".mtl";
            if (textureFilename != null)
            {
                using (StreamWriter file = new StreamWriter(mtlFilename))
                {
                    file.WriteLine("newmtl material0");
                    file.WriteLine("Ka 1.000000 1.000000 1.000000");
                    file.WriteLine("Kd 1.000000 1.000000 1.000000");
                    file.WriteLine("Ks 0.000000 0.000000 0.000000");
                    file.WriteLine("Tr 1.000000");
                    file.WriteLine("illum 1");
                    file.WriteLine("Ns 0.000000");
                    file.WriteLine("map_Kd " + Path.GetFileName(textureFilename));
                }
            }

            using (StreamWriter sw = new StreamWriter(filename))
            {
                if (textureFilename != null)
                {
                    sw.WriteLine("mtllib " + Path.GetFileName(mtlFilename));
                    sw.WriteLine("usemtl material0");
                }
                for (int i = 0; i < mesh.Vertices.Count; i++)
                {
                    if (mesh.HasColors && writeColors)
                    {
                        sw.WriteLine(string.Format("v {0} {1} {2} {3} {4} {5}", mesh.Vertices[i].Position.X.ToString("R"), mesh.Vertices[i].Position.Y.ToString("R"), mesh.Vertices[i].Position.Z.ToString("R"), mesh.Vertices[i].Color.R.ToString("R"), mesh.Vertices[i].Color.G.ToString("R"), mesh.Vertices[i].Color.B.ToString("R")));
                    }
                    else
                    {
                        sw.WriteLine(string.Format("v {0} {1} {2}", mesh.Vertices[i].Position.X.ToString("R"), mesh.Vertices[i].Position.Y.ToString("R"), mesh.Vertices[i].Position.Z.ToString("R")));
                    }
                }
                if(mesh.HasUVs)
                {
                    for (int i = 0; i < mesh.Vertices.Count; i++)
                    {
                        sw.WriteLine(string.Format("vt {0} {1}", mesh.Vertices[i].UV.U.ToString("R"), mesh.Vertices[i].UV.V.ToString("R")));
                    }
                }
                if (mesh.HasNormals)
                {
                    for (int i = 0; i < mesh.Vertices.Count; i++)
                    {
                        sw.WriteLine(string.Format("vn {0} {1} {2}", mesh.Vertices[i].Normal.X.ToString("R"), mesh.Vertices[i].Normal.Y.ToString("R"), mesh.Vertices[i].Normal.Z.ToString("R")));
                    }
                }
                foreach (Face f in mesh.Faces)
                {
                    string s = "";
                    if (!mesh.HasUVs && !mesh.HasNormals)
                    {
                        s = string.Format("f {0} {1} {2}", f.P0 + 1, f.P1 + 1, f.P2 + 1);
                    }
                    else if (mesh.HasUVs && !mesh.HasNormals)
                    {
                        s = string.Format("f {0}/{1} {2}/{3} {4}/{5}", f.P0 + 1, f.P0 + 1, f.P1 + 1, f.P1 + 1, f.P2 + 1, f.P2 + 1);
                    }
                    else if (!mesh.HasUVs && mesh.HasNormals)
                    {
                        s = string.Format("f {0}//{1} {2}//{3} {4}//{5}", f.P0 + 1, f.P0 + 1, f.P1 + 1, f.P1 + 1, f.P2 + 1, f.P2 + 1);
                    }
                    else if (mesh.HasUVs && mesh.HasNormals)
                    {
                        s = string.Format("f {0}/{1}/{2} {3}/{4}/{5} {6}/{7}/{8}", f.P0 + 1, f.P0 + 1, f.P0 + 1, f.P1 + 1, f.P1 + 1, f.P1 + 1, f.P2 + 1, f.P2 + 1, f.P2 + 1);
                    }
                    sw.WriteLine(s);
                }
            }
        }

        public override void Save(Mesh m, string filename, string imageFilename)
        {
            OBJSerializer.Write(m, filename, imageFilename);
        }

        public override Mesh Load(string filename)
        {
            return OBJSerializer.Read(filename);
        }

        public override string GetExtension()
        {
            return ".obj";
        }
    }
}
