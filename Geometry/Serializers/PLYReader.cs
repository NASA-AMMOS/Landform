using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;

namespace OPS.Geometry
{
    class PLYReader
    {
        protected delegate void PropertyApplyer(Vertex v, double value);
        protected delegate void MeshFlagger(Mesh m);

        enum Section
        {
            None,
            Vertex,
            Face
        }

        abstract class DataSegmentReader
        {
            public double ReadNextScaledValue(Property p)
            {
                return p.ScaleValue(ReadNext(p));
            }
            protected abstract double ReadNext(Property p);
        }

        class ASCIIDataSegmentReader : DataSegmentReader
        {
            StreamReader sr;
            string[] tokens = new string[] { };
            int i = 0;

            public ASCIIDataSegmentReader(StreamReader sr)
            {
                this.sr = sr;
                string line = sr.ReadLine();
                while (!line.Contains("end_header"))
                {
                    line = sr.ReadLine();
                }
            }

            string GetNextToken()
            {
                if (i == tokens.Length)
                {
                    i = 0;
                    tokens = sr.ReadLine().Split(null as string[],StringSplitOptions.RemoveEmptyEntries);
                }
                return tokens[i++];
            }

            protected override double ReadNext(Property p)
            {
                return double.Parse(GetNextToken());
            }
        }

        class BinaryDataSegmentReader : DataSegmentReader
        {
            BinaryReader br;
            public BinaryDataSegmentReader(BinaryReader br)
            {
                this.br = br;
                ReadUntilEndOfHeader(br);
            }

            protected override double ReadNext(Property p)
            {
                if(p.Type == typeof(byte))
                {
                    return br.ReadByte();
                }
                if (p.Type == typeof(int))
                {
                    return br.ReadInt32();
                }
                if (p.Type == typeof(uint))
                {
                    return br.ReadUInt32();
                }
                if (p.Type == typeof(float))
                {
                    return br.ReadSingle();
                }
                if (p.Type == typeof(double))
                {
                    return br.ReadDouble();
                }
                throw new PLYSerializerException("Unknown property type");
            }

            void ReadUntilEndOfHeader(BinaryReader br)
            {
                string endOfHeaderDelimeter = "end_header";
                string matchString = endOfHeaderDelimeter;
                int matchedSofar = 0;
                while (matchedSofar != matchString.Length)
                {
                    byte curByte = br.ReadByte();
                    if (matchString[matchedSofar] == curByte)
                    {
                        matchedSofar++;
                    }
                    else
                    {
                        matchedSofar = 0;
                    }
                }
                byte b = br.ReadByte();
                if (b == '\r')
                {
                    b = br.ReadByte();
                }
                if (b != '\n')
                {
                    throw new PLYSerializerException("Unexpected white space at end of ply header");
                }
            }
        }


        protected class Property
        {

            public Type Type;
            public PropertyApplyer Apply;

            
            public Property(Type type, PropertyApplyer applier = null)
            {
                this.Apply = applier;
                this.Type = type;                
            }

            public virtual double ScaleValue(double value)
            {
                return value;
            }
        }

        class ColorProperty : Property
        {
            public ColorProperty(Type type, PropertyApplyer applier) : base(type, applier) { }

            public override double ScaleValue(double value)
            {
                if(this.Type == typeof(byte))
                {
                    value = value / 255.0;
                }
                return value;
            }
        }

        string filename;                

        public PLYReader(string filename)
        {
            this.filename = filename;
            RegisterMeshFlaggers();
        }

        public void IgnoreApplyer(Vertex v, double value) { }

        Dictionary<string, MeshFlagger> meshFlaggers = new Dictionary<string, MeshFlagger>();
        
        protected virtual void RegisterMeshFlaggers()
        {
            MeshFlagger normals = (m) => { m.HasNormals = true; };
            MeshFlagger uvs = (m) => { m.HasUVs = true; };
            MeshFlagger colors = (m) => { m.HasColors = true; };

            meshFlaggers.Add("nx", normals);
            meshFlaggers.Add("ny", normals);
            meshFlaggers.Add("nz", normals);
            meshFlaggers.Add("texture_u", uvs);
            meshFlaggers.Add("texture_v", uvs);
            meshFlaggers.Add("s", uvs);
            meshFlaggers.Add("t", uvs);
            meshFlaggers.Add("red", colors);
            meshFlaggers.Add("green", colors);
            meshFlaggers.Add("blue", colors);
            meshFlaggers.Add("alpha", colors);
        }

        protected virtual Property CreateProperty(string propName, Type propType)
        {

            if(propName == "x")
            {
                return new Property(propType, (vertex, value) =>
                {
                    vertex.Position.X = value;
                });
            }
            if (propName == "y")
            {
                return new Property(propType, (vertex, value) =>
                {
                    vertex.Position.Y = value;
                });
            }
            if (propName == "z")
            {
                return new Property(propType, (vertex, value) =>
                {
                    vertex.Position.Z = value;
                });
            }
            if (propName == "nx")
            {
                return new Property(propType, (vertex, value) =>
                {
                    vertex.Normal.X = value;
                });
            }
            if (propName == "ny")
            {
                return new Property(propType, (vertex, value) =>
                {
                    vertex.Normal.Y = value;
                });
            }
            if (propName == "nz")
            {
                return new Property(propType, (vertex, value) =>
                {
                    vertex.Normal.Z = value;
                });
            }
            if (propName == "texture_u")
            {
                return new Property(propType, (vertex, value) =>
                {
                    vertex.UV.U = value;
                });
            }
            if (propName == "texture_v")
            {
                return new Property(propType, (vertex, value) =>
                {
                    vertex.UV.V = value;
                });
            }
            if (propName == "s")
            {
                return new Property(propType, (vertex, value) =>
                {
                    vertex.UV.U = value;
                });
            }
            if (propName == "t")
            {
                return new Property(propType, (vertex, value) =>
                {
                    vertex.UV.V = value;
                });
            }
            if(propName == "red")
            {
                return new ColorProperty(propType, (vertex, value) =>
                {
                    vertex.Color.R = value;
                });
            }
            if (propName == "green")
            {
                return new ColorProperty(propType, (vertex, value) =>
                {
                    vertex.Color.G = value;
                });
            }
            if (propName == "blue")
            {
                return new ColorProperty(propType, (vertex, value) =>
                {
                    vertex.Color.B = value;
                });
            }
            if (propName == "alpha")
            {
                return new ColorProperty(propType, (vertex, value) =>
                {
                    vertex.Color.A = value;
                });
            }
            return null;
        }

        /// <summary>
        /// Reads and a ply file
        /// </summary>
        /// <param name="textureFilename">This will be filled with the texture associated with the ply if there is one.  Null otherwise.</param>
        /// <param name="defaultAlpha">Some PLY files only contain RGB colors.  If so, use this value as the default alpha.  Suggested value is 1</param>
        /// <returns></returns>
        public Mesh Read(out string textureFilename, double defaultAlpha)
        {
            Mesh result = new Mesh();
            List<Property> vertexProps = new List<Property>();
            List<string> comments = new List<string>();
            textureFilename = null;
            int vertexCount = 0;
            int faceCount = 0;
            Section section = Section.None;

            // List of properties to read for each vertex
            List<Property> vertexProperties = new List<Property>();
            
            // Cache the uv properties so that we can select which one gets precidence later
            Property propertyU = null;
            Property propertyV = null;
            Property propertyS = null;
            Property propertyT = null;

            // Used to deterime what type face properties should be read
            Property faceIndexProperty = null;
            Property faceUVProperty = null;
                        
            bool isASCII;
            bool hasAlpha = false;
            using (StreamReader sr = new StreamReader(this.filename))
            {
                AssertEquals("ply", sr.ReadLine());
                string format = sr.ReadLine();
                if (format == "format binary_little_endian 1.0")
                {
                    isASCII = false;
                }
                else if (format == "format ascii 1.0")
                {
                    isASCII = true;
                }
                else
                {
                    throw new PLYSerializerException("Unknown ply format description");
                }

                Dictionary<string, Type> typeLookup = new Dictionary<string, Type>();
                typeLookup.Add("double", typeof(double));
                typeLookup.Add("float", typeof(float));
                typeLookup.Add("uchar", typeof(byte));
                typeLookup.Add("int", typeof(int));
                typeLookup.Add("uint", typeof(uint));

                while (true)
                {
                    string line = sr.ReadLine();
                    if (line.Contains("comment"))
                    {
                        string tmp = line.Replace("comment", "").Trim();
                        if (tmp.Contains(PLYSerializer.TextureFileCommentName))
                        {
                            textureFilename = tmp.Replace(PLYSerializer.TextureFileCommentName, "").Trim();
                        }
                        else
                        {
                            comments.Add(tmp);
                        }
                    }
                    else if (line.Contains("element vertex"))
                    {
                        string[] tokens = line.Split(' ');
                        vertexCount = int.Parse(tokens[2]);
                        result.Vertices.Capacity = vertexCount;
                        section = Section.Vertex;
                    }
                    else if (line.Contains("element face"))
                    {
                        string[] tokens = line.Split(' ');
                        faceCount = int.Parse(tokens[2]);
                        result.Faces.Capacity = faceCount;
                        section = Section.Face;
                    }
                    else if (line.Contains("property") && section == Section.Vertex)
                    {
                        string[] tokens = line.Split(' ');
                        string propName = tokens[2];
                        Type propType = typeLookup[tokens[1]];

                        if (meshFlaggers.ContainsKey(propName))
                        {
                            meshFlaggers[propName](result);
                        }

                        Property p = CreateProperty(propName, propType);
                        if(p == null)
                        {  
                            p = new Property(propType ,IgnoreApplyer);                            
                        }
                        vertexProperties.Add(p);

                        // Cache these for later so that we can select which one takes precidence
                        if (propName == "texture_u")
                        {
                            propertyU = p;
                        }
                        if (propName == "texture_v")
                        {
                            propertyV = p;
                        }
                        if (propName == "s")
                        {
                            propertyS = p;
                        }
                        if (propName == "t")
                        {
                            propertyT = p;
                        }
                        if(propName == "alpha")
                        {
                            hasAlpha = true;
                        }
                    }
                    else if (line.Contains("property list") && section == Section.Face)
                    {
                        if(line.Contains("uchar int vertex_indices"))
                        {
                            faceIndexProperty = new Property(typeof(int));
                        }
                        else if (line.Contains("uchar uint vertex_indices"))
                        {
                            faceIndexProperty = new Property(typeof(uint));
                        }
                        else if(line.Contains("property list uchar float texcoord"))
                        {
                            result.HasUVs = true;
                            faceUVProperty = new Property(typeof(float));
                        }
                        else if (line.Contains("property list uchar double texcoord"))
                        {
                            result.HasUVs = true;
                            faceUVProperty = new Property(typeof(double));
                        }
                        else
                        {
                            throw new PLYSerializerException("Unknown face property");
                        }
                    }
                    else if (line.Contains("end_header"))
                    {
                        break;
                    }
                }
            }

            // Now that we have finished reading the header, choose which UVs to pay attention to in the case that mutliple properties are defined
            // Prefer s and t over texture_u and texture_v            
            if (propertyS != null && propertyU != null)
            {
                propertyU.Apply = IgnoreApplyer;
            }
            if (propertyT != null && propertyV != null)
            {
                propertyV.Apply = IgnoreApplyer;
            }
            // We make the reasonable assumption that if s or t are defined, they both are.  Same with u and v
            // If any vertex based uv is defined we ignore the face uvs
            bool ignoreFaceUVs = propertyS != null || propertyT != null ||  propertyU != null || propertyV != null;

            if (isASCII)
            {
                using (StreamReader sr = new StreamReader(filename))
                {
                    ASCIIDataSegmentReader reader = new ASCIIDataSegmentReader(sr);
                    ReadBody(result, vertexCount, faceCount, vertexProperties, reader, faceIndexProperty, faceUVProperty, ignoreFaceUVs);
                }            
            }
            else
            {
                FileStream fs = File.OpenRead(filename);
                using (BinaryReader br = new BinaryReader(fs))
                {
                    BinaryDataSegmentReader reader = new BinaryDataSegmentReader(br);
                    ReadBody(result, vertexCount, faceCount, vertexProperties, reader, faceIndexProperty, faceUVProperty, ignoreFaceUVs);
                    if (fs.Position != fs.Length)
                    {
                        throw new PLYSerializerException("Unexpected end of PLY file");
                    }
                }
            }
            if(vertexCount != result.Vertices.Count  || faceCount != result.Faces.Count)
            {
                throw new PLYSerializerException("Unexpected number of vertices or faces in PLY file");
            }

            if (result.HasColors && !hasAlpha)
            {
                for(int i = 0; i < result.Vertices.Count; i++)
                {
                    result.Vertices[i].Color.A = defaultAlpha;
                }
            }

            return result;
        }

        void ReadBody(Mesh result, int vertexCount, int faceCount, List<Property> vertexProperties, DataSegmentReader reader, Property faceIndexProperty, Property faceUVProperty, bool ignoreFaceUVs)
        {
            for (int i = 0; i < vertexCount; i++)
            {
                Vertex v = new Vertex();
                for (int j = 0; j < vertexProperties.Count; j++)
                {
                    Property p = vertexProperties[j];
                    p.Apply(v, reader.ReadNextScaledValue(p));
                }
                result.Vertices.Add(v);
            }
            Property byteProperty = new Property(typeof(byte));
            for (int i = 0; i < faceCount; i++)
            {
                byte len = (byte)reader.ReadNextScaledValue(byteProperty);
                if (len != 3)
                {
                    throw new PLYSerializerException("Unexpected number of vertices in face.  Expected 3 but got " + len);
                }
                Face f = new Face();
                f.P0 = (int)reader.ReadNextScaledValue(faceIndexProperty);
                f.P1 = (int)reader.ReadNextScaledValue(faceIndexProperty);
                f.P2 = (int)reader.ReadNextScaledValue(faceIndexProperty);
                result.Faces.Add(f);
                if (faceUVProperty != null)
                {
                    len = len = (byte)reader.ReadNextScaledValue(byteProperty);
                    if (len != 6)
                    {
                        throw new PLYSerializerException("Unexpected number of uvs in face.  Expected 6 but got " + len);
                    }
                    Vector2 uv0 = new Vector2(reader.ReadNextScaledValue(faceUVProperty), reader.ReadNextScaledValue(faceUVProperty));
                    Vector2 uv1 = new Vector2(reader.ReadNextScaledValue(faceUVProperty), reader.ReadNextScaledValue(faceUVProperty));
                    Vector2 uv2 = new Vector2(reader.ReadNextScaledValue(faceUVProperty), reader.ReadNextScaledValue(faceUVProperty));
                    if (ignoreFaceUVs)
                    {
                        // This ply defines both vertex and face uvs. Sanity check they are the same
                        if (!uv0.AlmostEqual(result.Vertices[f.P0].UV) ||
                            !uv1.AlmostEqual(result.Vertices[f.P1].UV) ||
                            !uv2.AlmostEqual(result.Vertices[f.P2].UV))
                        {
                            throw new PLYSerializerException("Face uvs do not match vertex uvs");
                        }
                    }
                    else
                    {
                        result.Vertices[f.P0].UV = uv0;
                        result.Vertices[f.P1].UV = uv1;
                        result.Vertices[f.P2].UV = uv2;
                    }
                }
            }
        }

        static void AssertEquals(string expected, string value)
        {
            if (expected != value)
            {
                throw new PLYSerializerException("Error: expected " + expected + " but got " + value);
            }
        }
    }
}
