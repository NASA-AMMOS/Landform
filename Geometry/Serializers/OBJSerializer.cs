//#define FAST_PARSE
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Diagnostics;
using Microsoft.Xna.Framework;

namespace JPLOPS.Geometry
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
        /// <param name="defaultAlpha">OBJ doesn't support vertex colors but some tools write the RGB component anyway.
        /// Use this value to set the alpha</param>
        /// <param name="capacity">Optional starting capacity for mesh data structure</param>
        /// <returns></returns>
        public static Mesh Read(string filename, out string textureFilename,
                                double defaultAlpha = 1, int capacity = 0, bool onlyGetImageFilename = false)
        {
            textureFilename = null;

            if (!File.Exists(filename))
            {
                throw new IOException($"OBJ mesh {filename} not found");
            }

            //OBJ mesh filesize in bytes is about 112 * numTris
            //
            //M20 tactical meshes have about about ~0.5 v per f
            //exactly 3 vt per f
            //zero vn (M20 tactical mesh vertex normals are generated from faces)
            //
            //v  -1.643300 30.438145 -5.858544 #~34 bytes
            //vt 0.1280 0.3075 #~18 bytes
            //f 10475/105268 10425/105269 10456/105270 #~41 bytes
            //
            //assuming ~0.5 v per f and 3 vt per f: objBytes = (0.5*34 + 3*18 + 41) * numTris = 112 * numTris
            //
            //experimentally:
            //600k verts, 1.15M faces, 133M bytes (expected 112 * 1.15M = 129M)
            //160k verts, 300k faces, 34M bytes (expected 112 * 300k = 34M)
            //41k verts, 76k faces, 8.4M bytes (expected 112 * 76k = 8.58.58.58.58.58.58.58.5M)

            List<Vector3> vertices = new List<Vector3>();
            List<Vector4> colors = new List<Vector4>();
            List<Vector2> uvs = new List<Vector2>();
            List<Vector3> normals = new List<Vector3>();
            List<OBJFace> objFaces = new List<OBJFace>();

            MTLFile mtl = null;

            if (capacity > 0)
            {
                vertices.Capacity = capacity;
                colors.Capacity = capacity;
                uvs.Capacity = capacity;
                normals.Capacity = capacity;
                objFaces.Capacity = capacity;
            }
            else
            {
                double bytes = new FileInfo(filename).Length;
                double nt = 1.1 * bytes / 112;
                double nv = 1.1 * nt / 2;
                double nvt = nt * 3;
                vertices.Capacity = (int)nv;
                uvs.Capacity = (int)nvt;
                objFaces.Capacity = (int)nt;
                //if (!onlyGetImageFilename) Console.WriteLine("{0} {1} bytes, {2} tris", filename, Fmt.KMG(bytes), Fmt.KMG(nt));
            }

            // OBJs can contain different length arrays of vert, uv, normals.
            // Thus each face indices each of these attributes individually.
            // We use lists to temporarily store the vert, uv, normals, and 
            // faces in the more complicated structure and then convert them 
            // into a one-2-one indexing scheme where all attribute arrays are 
            // the same length.           
                
            var sw = Stopwatch.StartNew();
            using (StreamReader sr = new StreamReader(filename))
            {
                for (int lineNum = 1; true; lineNum++)
                {
                    string line = sr.ReadLine();
                    if (line == null)
                    {
                        break; //eof
                    }

                    try
                    {
                        string[] tok = line.Split().Where(s => s.Length != 0).ToArray();
                        if (tok.Length < 2 || tok[0].StartsWith("#"))
                        {
                            continue;
                        }
                        
                        switch (tok[0])
                        {
                            case "mtllib":
                            {
                                string mtlFile = Path.Combine(Path.GetDirectoryName(filename), tok[1]);
                                if (File.Exists(mtlFile))
                                {
                                    mtl = new MTLFile(mtlFile);
                                }
                                break;
                            }
                            case "usemtl":
                            {
                                textureFilename = mtl != null ? mtl.GetTextureFile(tok[1]) : null;
                                if (onlyGetImageFilename)
                                {
                                    return null;
                                }
                                break;
                            }
                            case "v":
                            {
                                vertices.Add(new Vector3(ParseFloat(tok[1]), ParseFloat(tok[2]), ParseFloat(tok[3])));
                                //obj doesn't offically support vertex colors but some tools pack them after xyz
                                if (tok.Length >= 7)
                                {
                                    double a = tok.Length >= 8 ? ParseFloat(tok[7]) : defaultAlpha;
                                    colors.Add(new Vector4(ParseFloat(tok[4]), ParseFloat(tok[5]),
                                                           ParseFloat(tok[6]), a));
                                }
                                break;
                            }
                            case "vt":
                            {
                                uvs.Add(new Vector2(ParseFloat(tok[1]), ParseFloat(tok[2])));
                                break;
                            }
                            case "vn":
                            {
                                normals.Add(new Vector3(ParseFloat(tok[1]), ParseFloat(tok[2]), ParseFloat(tok[3])));
                                break;
                            }
                            case "f":
                            {
                                OBJFace f = new OBJFace();
                                for (int i = 0; i < 3; i++)
                                {
                                    string[] ptTok = tok[i + 1].Split('/');
                                    f.vertDef[i].vertIdx = ParseInt(ptTok[0]) - 1;
                                    if (ptTok.Length > 1 && ptTok[1].Length > 0)
                                    {
                                        f.vertDef[i].uvIdx = ParseInt(ptTok[1]) - 1;
                                    }
                                    if (ptTok.Length > 2 && ptTok[2].Length > 0)
                                    {
                                        f.vertDef[i].normalIdx = ParseInt(ptTok[2]) - 1;
                                    }
                                }
                                objFaces.Add(f);
                                break;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        throw new OBJSerializerException($"error parsing OBJ {filename} line {lineNum} ({line})", ex);
                    }
                }
            }

            //if (!onlyGetImageFilename) Console.WriteLine("parse OBJ: {0}", Fmt.HMS(sw));
            sw.Restart();
                
            Mesh result = new Mesh();
            result.HasNormals = normals.Count != 0;
            result.HasUVs = uvs.Count != 0;
            result.HasColors = colors.Count != 0;
            if (result.HasColors && vertices.Count != colors.Count)
            {
                throw new OBJSerializerException("Not all vertices in OBJ defined colors.  " +
                                                 "If any vertex defines a color then they all must");
            }
            if (objFaces.Count == 0)
            {
                //This is a weird OBJ file which doesn't define any faces.
                //The spec is unclear on how to interpret the relationship between vertices and
                //uvs/normals in this case.
                //We make the assumption that in the absence of faces,
                //the uv and normal elements have a one-to-one mapping with vertices.
                //Thus, if either list is defined it must also be the same lenght as the list of vertices.
                //This assumption allows us to read in obj point clouds.
                if (result.HasUVs && uvs.Count != vertices.Count)
                {
                    throw new OBJSerializerException("OBJ did not contain face description " +
                                                     "and number of vertices and uvs differs");
                }
                if (result.HasNormals && normals.Count != vertices.Count)
                {
                    throw new OBJSerializerException("OBJ did not contain face description " +
                                                     "and number of vertices and uvs differs");
                }
                result.Vertices.Capacity = vertices.Count;
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
                //This is a normal obj file.
                //Generate a mesh using the faces.
                //Any vertices or uvs not referenced by a face will be ommitted.
                var vertDefToIndex = new Dictionary<VertexDefinition, int>(3 * objFaces.Count);
                result.Vertices.Capacity = 3 * objFaces.Count;
                result.Faces.Capacity = objFaces.Count;
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
                result.Vertices.TrimExcess();
            }

            //if (!onlyGetImageFilename) Console.WriteLine("buid mesh: {0}", Fmt.HMS(sw));

            return result;
        }

#if FAST_PARSE

        //these give about a 25% speedup
        //but leaving disabled for now as it's late in the G6.8 release cycle
        //and the speedup doesn't seem worth the risk
        //for a large 3.5GB obj with about 25M tris the timings (just to parse, not including building the mesh) are
        //without FAST_PARSE: 1m53s
        //with FAST_PARSE (long implementation): 1m26s
        //with FAST_PARSE (int implementation): 1m24s

        private static float ParseFloat(string str)
        {
            float ret = StringHelper.FastParseFloat(str);
            //double ck = double.Parse(str);
            //double diff = Math.Abs(ck - ret);
            //if (diff > 1e-6) Console.WriteLine("str={0}, fastParse={1}, parse={2}, diff={3}", str, ret, ck, diff);
            return ret;
        }

        private static int ParseInt(string str)
        {
            return StringHelper.FastParseInt(str);
        }

#else

        private static float ParseFloat(string str)
        {
            return (float)(double.Parse(str));
        }

        private static int ParseInt(string str)
        {
            return int.Parse(str);
        }

#endif

        public static Mesh Read(string filename, double defaultAlpha = 1, int capacity = 0)
        {
            return Read(filename, out string textureFilename, defaultAlpha, capacity);
        }

        /// <summary>
        /// Saves a mesh out as an obj file.  Note that obj format does not offically support
        /// color vertex attributes so these will be lost.  Only position, uv, and normals will be 
        /// written out.  If the optional textureFilename is included a .mtl file will be created with the
        /// same name as the image specifying to use the supplied image as a diffuse texture.
        /// </summary>
        /// <param name="mesh">Mesh to export</param>
        /// <param name="filename">Output filename</param>
        /// <param name="textureFilename">Optional diffuse texture to include as a material</param>
        public static void Write(Mesh mesh, string filename, string textureFilename = null, bool writeColors = true)
        {
            string mtlFilename = null;
            if (textureFilename != null)
            {
                //important: material filename is in same directory as obj but has name foo.mtl
                //where the texture file is e.g. foo.jpg
                //this covers the case where the obj file being written is a temp file, e.g. GUID.obj
                //and will be moved or uploded to a different final destination like foo.obj
                //in that case the passed textureFilename could be something like foo.jpg
                //ant then the mtllib ref that gets written into GUID.obj is foo.mtl
                //so that in the tmp dir we have GUID.obj and foo.mtl
                //and in the final destination we can have foo.obj, foo.mtl, and foo.jpg
                mtlFilename = Path.Combine(Path.GetDirectoryName(filename),
                                           Path.GetFileNameWithoutExtension(textureFilename)) + ".mtl";
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
            Write(m, filename, imageFilename);
        }

        public override Mesh Load(string filename)
        {
            return OBJSerializer.Read(filename);
        }

        public override Mesh Load(string filename, out string imageFilename, bool onlyGetImageFilename = false)
        {
            return OBJSerializer.Read(filename, out imageFilename, onlyGetImageFilename: onlyGetImageFilename);
        }

        public override List<Mesh> LoadAllLODs(string filename)
        {
            return LoadAllLODs(filename, out string imageFilename);
        }

        //this impl is limited to loading up to 100 total LODs
        //(or 10 if filename does not end in _LODn_m or _LODnn_mm)
        //extra LODs must be siblings with a suffix like _LODn, _LODnn, _LODn_m, _LODnn_mm
        //note: filename is always the finest loaded LOD, and it may or may not have an _LOD suffix
        //if filename does not have an _LOD suffix then n must start at 1
        //if filename does have an _LOD suffix then that defines the starting value of n
        //if filename matches pfx_LOD[0]1[_m[m]] and pfx also exists it is loaded as the first (finest) LOD
        //LODs are read in contiguous order up to the first missing one or n = m
        public override List<Mesh> LoadAllLODs(string filename, out string imageFilename,
                                               bool onlyGetImageFilename = false)
        {
            var primaryMesh = Load(filename, out imageFilename, onlyGetImageFilename);

            if (onlyGetImageFilename)
            {
                return null;
            }

            List<Mesh> lods = new List<Mesh>();

            string ext = Path.GetExtension(filename); //includes dot
            string bn = filename.Substring(0, filename.Length - ext.Length);

            //100 should work but would result in over 100 File.Exists() tests
            //when filename does not have an _LOD suffix
            //and there are not actually any coarser LODs to load
            //int maxLODs = 100;
            int maxLODs = 10;

            int firstLOD = 0, lastLOD = maxLODs - 1, nWidth = 0, mWidth = 0;

            string sfx = "_LOD";
            string pat = @"(.+)" + sfx + @"(\d+)(?:_(\d+))?$";

            var match = Regex.Match(bn, pat);
            if (match.Success) //primary mesh has _LOD suffix
            {
                bn = match.Groups[1].Value;
                string n = match.Groups[2].Value;
                nWidth = n.Length;
                firstLOD = int.Parse(n);
                string m = match.Groups[3].Value;
                if (!string.IsNullOrEmpty(m))
                {
                    mWidth = m.Length;
                    lastLOD = int.Parse(m);
                }
                if (firstLOD == 1 && File.Exists(bn + ext)) //if non _LOD mesh exists, load it as first (finest) LOD
                {
                    lods.Add(Load(bn + ext));
                }
            }
            else
            {
                if (File.Exists(bn + sfx + "1" + ext))
                {
                    nWidth = 1;
                }
                else if (File.Exists(bn + sfx + "01" + ext))
                {
                    nWidth = 2;
                }
                else
                {
                    for (int m = 1; m < maxLODs; m++)
                    {
                        if (m < 10 && File.Exists(bn + sfx + "1_" + m + ext))
                        {
                            nWidth = 1;
                            mWidth = 1;
                            lastLOD = m;
                            break;
                        }
                        if (File.Exists(bn + sfx + "01_" + m.ToString("00") + ext))
                        {
                            nWidth = 2;
                            mWidth = 2;
                            lastLOD = m;
                            break;
                        }
                    }
                }
            }

            lods.Add(primaryMesh);

            //load remaining LODs
            string nFmt = new string('0', nWidth);
            string mFmt = new string('0', mWidth);
            for (int lod = firstLOD + 1; lod <= lastLOD; lod++)
            {
                string lodFile =
                    bn + sfx + lod.ToString(nFmt) + (mWidth > 0 ? ("_" + lastLOD.ToString(mFmt)) : "") + ext;
                if (!File.Exists(lodFile))
                {
                    break;
                }
                lods.Add(Load(lodFile));
            }

            return lods;
        }

        public override string GetExtension()
        {
            return ".obj";
        }
    }
}
