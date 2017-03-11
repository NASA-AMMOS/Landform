using System.Collections.Generic;
using System.Linq;
using System.IO;
using Microsoft.Xna.Framework;
using OPS.Geometry;

namespace OPS.Geometry
{
    /// <summary>
    /// Very basic parser for OpenInventor format used to represent IDS navcam wedge mesh products.
    /// This parser does not fully implemnt the Open Inventor format; it only supports the minimal
    /// feature set to read IDS mesh products and convert them to the Mesh class representation.
    /// </summary>
    class OpenInventorParser
    {
        private string filename;

        public OpenInventorParser(string filename)
        {
            this.filename = filename;
        }

        private IEnumerable<string> readTokens(StreamReader fileReader)
        {
            char[] separators = " \t\n".ToCharArray();
            string line;
            while ((line = fileReader.ReadLine()) != null)
            {
                string[] tokens = line.Split(separators);
                foreach (string token in tokens)
                {
                    if (!string.IsNullOrEmpty(token))
                    {
                        yield return token;
                    }
                }
            }
            yield break;
        }

        private IEnumerable<string> tokens;

        private IEnumerator<string> tokenEnumerator;

        List<MeshInfo> meshes = new List<MeshInfo>();

        class LevelOfDetail
        {
            public List<MeshInfo> meshes = new List<MeshInfo>();
        }

        class MeshInfo
        {
            public int lod;
            public List<Vector3> vertices = new List<Vector3>();
            public List<Vector2> texCoords = new List<Vector2>();
            public List<int> triStripIndices = new List<int>();

            public MeshInfo(int lod)
            {
                this.lod = lod;
            }

            public bool FullySpecified
            {
                get
                {
                    return
                        this.vertices.Count > 0 &&
                        this.texCoords.Count > 0 &&
                        this.triStripIndices.Count > 0;
                }
            }

            public Mesh ToMesh()
            {
                List<Face> faces = new List<Face>();

                List<Vector3> denormalizedVerts = new List<Vector3>();
                List<Vector2> denormalizedTexCoords = new List<Vector2>();

                int i = 0;
                int triCount = 0;
                int texCoordIndex = 0;
                while (i < this.triStripIndices.Count - 2)
                {
                    if (this.triStripIndices[i + 2] == -1)
                    {
                        i += 3;             // Skip to next strip
                        texCoordIndex += 2; // Texture coords skip less than indices, because there is no -1 terminator in tex coord array
                        triCount = 0;
                        continue;
                    }

                    Face tri;
                    Vector3 v0, v1, v2;
                    Vector2 uv0, uv1, uv2;
                    if (triCount % 2 == 0)
                    {
                        v0 = this.vertices[this.triStripIndices[i]];
                        v1 = this.vertices[this.triStripIndices[i + 1]];
                        v2 = this.vertices[this.triStripIndices[i + 2]];

                        uv0 = this.texCoords[texCoordIndex];
                        uv1 = this.texCoords[texCoordIndex + 1];
                        uv2 = this.texCoords[texCoordIndex + 2];
                    }
                    else
                    {
                        v0 = this.vertices[this.triStripIndices[i]];
                        v1 = this.vertices[this.triStripIndices[i + 2]];
                        v2 = this.vertices[this.triStripIndices[i + 1]];

                        uv0 = this.texCoords[texCoordIndex];
                        uv1 = this.texCoords[texCoordIndex + 2];
                        uv2 = this.texCoords[texCoordIndex + 1];
                    }

                    int baseIndex = denormalizedVerts.Count;
                    tri = new Face(baseIndex, baseIndex + 1, baseIndex + 2);

                    denormalizedVerts.Add(v0);
                    denormalizedVerts.Add(v1);
                    denormalizedVerts.Add(v2);

                    denormalizedTexCoords.Add(uv0);
                    denormalizedTexCoords.Add(uv1);
                    denormalizedTexCoords.Add(uv2);

                    faces.Add(tri);
                    triCount += 1;
                    i += 1;
                    texCoordIndex += 1;
                }

                Mesh newMesh = new Mesh(hasUVs: true);                
                for(int j = 0; j < denormalizedVerts.Count; j++)
                {
                    Vertex v = new Vertex(denormalizedVerts[j]);
                    v.UV = denormalizedTexCoords[j];
                    newMesh.Vertices.Add(v);
                }
                newMesh.Faces = faces;
                return newMesh;
            }
        }

        public List<Mesh> Parse()
        {
            using (StreamReader fileReader = new StreamReader(this.filename))
            {
                fileReader.ReadLine(); // Skip header line

                this.tokens = readTokens(fileReader);
                this.tokenEnumerator = this.tokens.GetEnumerator();

                MeshInfo currentMeshInfo = null;

                List<LevelOfDetail> levelsOfDetail = new List<LevelOfDetail>();

                string token = null;

                int lodCounter = 0;

                // Note: this parser does not follow the heirarchy of the inventor file. It simply scans
                // for LevelOfDetail nodes and then treats the following geometry nodes as mesh for that set of LOD's.
                while (true)
                {
                    token = NextToken();

                    if (token == null)
                    {
                        break; // End of file
                    }

                    if (token == "LevelOfDetail")
                    {
                        lodCounter = 0;
                    }
                    else if (token == "Separator")
                    {
                        if (currentMeshInfo == null || currentMeshInfo.FullySpecified)
                        {
                            // Start new mesh info
                            lodCounter += 1;
                            currentMeshInfo = new MeshInfo(lodCounter);
                            meshes.Add(currentMeshInfo);
                        }
                        else
                        {
                            //ignore, continue current mesh
                        }

                    }
                    else if (token == "Coordinate3")
                    {
                        //Node coordinate3 = new Node("Coordinate3");
                        //currentNode.AddChild(coordinate3);

                        currentMeshInfo.vertices = ReadVector3List();
                    }
                    else if (token == "TextureCoordinate2")
                    {
                        currentMeshInfo.texCoords = ReadVector2List();
                    }
                    else if (token == "IndexedTriangleStripSet")
                    {
                        currentMeshInfo.triStripIndices = ReadIntList();
                    }
                    // ignore all other tokens
                }

                int lodCount = meshes.Max(m => m.lod);

                List<Mesh> outMeshes = new List<Mesh>();
                for (int lodNum = 1; lodNum <= lodCount; lodNum++)
                {
                    Mesh[] meshAtThisLOD = meshes.Where(m => m.vertices.Count > 0)
                        .Where(m => m.lod == lodNum)
                        .Select(m => m.ToMesh())
                        .ToArray();

                    outMeshes.Add(Mesh.Merge(meshAtThisLOD));
                }
                return outMeshes;
            }
        }

        private string NextToken()
        {
            if (this.tokenEnumerator.MoveNext())
            {
                return this.tokenEnumerator.Current;
            }
            return null;
        }

        private List<Vector3> ReadVector3List()
        {
            NextToken();  // {
            NextToken();  // point
            NextToken();  // [

            List<Vector3> vertices = new List<Vector3>();

            while (true)
            {
                string vert1 = NextToken();
                if (vert1 == "]")
                    break;

                string vert2 = NextToken();
                string vert3 = NextToken().Trim(',');

                Vector3 vert = new Vector3(
                    double.Parse(vert1),
                    double.Parse(vert2),
                    double.Parse(vert3));

                vertices.Add(vert);
            }
            NextToken(); // }

            return vertices;
        }

        private List<Vector2> ReadVector2List()
        {
            NextToken();  // {
            NextToken();  // point
            NextToken();  // [

            List<Vector2> coords = new List<Vector2>();

            while (true)
            {
                string vert1 = NextToken();
                if (vert1 == "]")
                    break;

                string vert2 = NextToken().Trim(',');

                Vector2 vert = new Vector2(
                    double.Parse(vert1),
                    double.Parse(vert2));

                coords.Add(vert);
            }
            NextToken(); // }

            return coords;
        }

        private List<int> ReadIntList()
        {
            NextToken();  // {
            NextToken();  // coordIndex
            NextToken();  // [

            List<int> coords = new List<int>();

            string index = NextToken().Trim(',');
            while (index != "]")
            {
                coords.Add(int.Parse(index));
                index = NextToken().Trim(',');
            }
            NextToken(); // }

            return coords;
        }
    }
}