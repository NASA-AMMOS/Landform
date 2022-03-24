using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Microsoft.Xna.Framework;

namespace JPLOPS.Geometry
{
    public class STLSerializer : MeshSerializer
    {
        public override string GetExtension()
        {
            return ".stl";
        }

        /// <summary>
        /// Reads an ascii or binary stl file
        /// Assigns face normals specified in stl to each vertex normal
        /// You may wish to clear the normals off of the mesh and call clean to reduce vertex count if you know that the vertex normals
        /// </summary>
        /// <param name="filename"></param>
        /// <returns></returns>
        public override Mesh Load(string filename)
        {
            List<Triangle> triangles = new List<Triangle>();            
            bool isBinary = true;
            // Start by trying to read the file as binary
            using (FileStream fs = new FileStream(filename, FileMode.Open, FileAccess.Read))
            {                
                using (BinaryReader br = new BinaryReader(fs))
                {
                    byte[] header = br.ReadBytes(80);
                    string headerString = System.Text.Encoding.UTF8.GetString(header);
                    if (headerString.Trim().StartsWith("solid"))
                    {
                        // oops this is a ascii stl
                        isBinary = false;
                    }
                    else
                    { 
                        UInt32 numTriangles = br.ReadUInt32();
                        for (UInt32 i = 0; i < numTriangles; i++)
                        {
                            Vector3 n = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                            Vector3 p0 = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                            Vector3 p1 = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                            Vector3 p2 = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                            UInt16 attributeBytes = br.ReadUInt16();

                            Vertex v0 = new Vertex(p0, n);
                            Vertex v1 = new Vertex(p1, n);
                            Vertex v2 = new Vertex(p2, n);
                            triangles.Add(new Triangle(v0, v1, v2));
                        }
                    }
                }
            }

            if (!isBinary)
            {
                // Read as ascii
                using (FileStream fs = new FileStream(filename, FileMode.Open, FileAccess.Read))
                {
                    using(StreamReader sr = new StreamReader(fs))
                    {
                        var line = sr.ReadLine().Trim();
                        if(!line.Contains("solid"))
                        {
                            throw new MeshSerializerException("'solid' not found in ASCII stl file");
                        }
                        while(!sr.EndOfStream)
                        {
                            line = sr.ReadLine().Trim();       
                            if(line.Contains("endsolid"))
                            {
                                break;
                            }       
                            var parts = line.Split().Where(p => p.Trim().Length != 0).ToArray();
                            if (parts.Length != 5 || !parts[0].Contains("facet") || !parts[1].Contains("normal") )
                            {
                                throw new MeshSerializerException("'facet normal' not found in ASCII stl file");
                            }
                            Vector3 normal = new Vector3(double.Parse(parts[2]), double.Parse(parts[3]), double.Parse(parts[4]));
                            line = sr.ReadLine();
                            if(!line.Contains("outer"))
                            {
                                throw new MeshSerializerException("'outer loop' not found in ASCII stl file");
                            }
                            Triangle t = new Triangle(ReadVertex(sr.ReadLine()),
                                                      ReadVertex(sr.ReadLine()),
                                                      ReadVertex(sr.ReadLine()));
                            t.V0.Normal = t.V1.Normal = t.V2.Normal = normal;
                            triangles.Add(t);
                            line = sr.ReadLine();
                            if (!line.Contains("endloop"))
                            {
                                throw new MeshSerializerException("'endloop' not found in ASCII stl file");
                            }
                            line = sr.ReadLine();
                            if (!line.Contains("endfacet"))
                            {
                                throw new MeshSerializerException("'endfacet' not found in ASCII stl file");
                            }
                        }
                    }
                }
            }
            return new Mesh(triangles, hasNormals: true); 
        }

        Vertex ReadVertex(string line)
        {
            line = line.Trim();
            var parts = line.Split().Where(p => p.Trim().Length != 0).ToArray();
            if(parts.Length != 4 || !parts[0].Contains("vertex"))
            {
                throw new MeshSerializerException("'vertex' not found in ASCII stl file");
            }
            return new Vertex(double.Parse(parts[1]), double.Parse(parts[2]), double.Parse(parts[3]));
        }

        /// <summary>
        /// Saves a mesh as an STL file, uses face normals and ignores vertex normals
        /// </summary>
        /// <param name="m"></param>
        /// <param name="filename"></param>
        /// <param name="imageFilename"></param>
        public override void Save(Mesh m, string filename, string imageFilename)
        {
            using (FileStream fs = new FileStream(filename, FileMode.Create, FileAccess.Write))
            {
                using (BinaryWriter bw = new BinaryWriter(fs))
                {
                    byte[] header = System.Text.Encoding.UTF8.GetBytes("STL created by Landform");
                    bw.Write(header);
                    bw.Write(new byte[80 - header.Length]);
                    bw.Write(m.Faces.Count);
                    foreach(var t in m.Triangles())
                    {
                        Vector3 n = t.Normal;
                        bw.Write((float)n.X);
                        bw.Write((float)n.Y);
                        bw.Write((float)n.Z);
                        bw.Write((float)t.V0.Position.X);
                        bw.Write((float)t.V0.Position.Y);
                        bw.Write((float)t.V0.Position.Z);
                        bw.Write((float)t.V1.Position.X);
                        bw.Write((float)t.V1.Position.Y);
                        bw.Write((float)t.V1.Position.Z);
                        bw.Write((float)t.V2.Position.X);
                        bw.Write((float)t.V2.Position.Y);
                        bw.Write((float)t.V2.Position.Z);
                        bw.Write((UInt16)0);
                    }
                }                
            }
        }
    }
}
