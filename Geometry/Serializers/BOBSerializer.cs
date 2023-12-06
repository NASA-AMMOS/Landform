using Microsoft.Xna.Framework;
using System;
using System.IO;

namespace JPLOPS.Geometry
{

    public class BOBSerializerException : MeshSerializerException
    {
        public BOBSerializerException() { }
        public BOBSerializerException(string message) : base(message) { }
        public BOBSerializerException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// Legacy binary m format that is similar to obj
    /// </summary>
    public class BOBSerializer : MeshSerializer
    {

        /// Bob file format
        ///     A binary obj (or .bob) file is a made up format for efficently storing and streaming obj files 
        ///     Note that unlike an obj, all indices start at 0
        ///     0.  1 ushort file version number
        ///     1.	1 ushort number of vertices
        ///     2.	1 ushort number of texture coordinates
        ///     3.	1 ushort number of normal
        ///     4.	1 ushort number of faces
        ///     5.	A list of vertices, for each vertex, 3 Float32 numbers representing x,y,z
        ///     6.	A list of texture coordinates, for each vertex, 2 float32 numbers representing u,v
        ///     7.	A list of normal, for each vertex, 3 float32 numbers representing nx, ny, nz
        ///     8.	A list of faces.  Bob files are naive or one-to-one and only support a single index for all attributes
        ///         For each face
        ///            ushort P0 vertex index, ushort P1 vertex index, ushort P2 vertex index
        ///     9.  1 ushort grid size
        ///    10.  3 floats for m min xyz bound
        ///    11.  3 floats for m max xyz bound
        ///    12.  1 ushort for total number of buckets (redundant)
        ///    13.  For each bucket
        ///            1 ushort with the number of faces in that bucket
        ///    14.  For each bucket
        ///            For each face in the bucket as specified in 13
        ///               1 ushort with the face index

        const int VERSION_NUM = 5;
        const int GRID_SIZE = 0; // Always zero, this functionality is depricated

        public override string GetExtension()
        {
            return ".bob";
        }

        public override Mesh Load(string filename)
        {
            Mesh m = new Mesh();
            using (BinaryReader br = new BinaryReader(File.Open(filename, FileMode.Open)))
            {
                UInt16 fileVersionNumber = br.ReadUInt16();
                if (fileVersionNumber != VERSION_NUM)
                {
                    throw new BOBSerializerException("Bob version number mismatch");
                }
                UInt32 numVerts = br.ReadUInt16();
                UInt32 numUvs = br.ReadUInt16();
                UInt32 numNorms = br.ReadUInt16();
                UInt32 numFaces = br.ReadUInt16();


                for (int i = 0; i < numVerts; i++)
                {
                    m.Vertices.Add(new Vertex(br.ReadSingle(), 
                                              br.ReadSingle(), 
                                              br.ReadSingle()));
                }

                if (numUvs > 0)
                {
                    m.HasUVs = true;
                    if (numUvs != numVerts)
                    {
                        throw new BOBSerializerException("UV count must match vertex length");
                    }
                    for (int i = 0; i < m.Vertices.Count; i++)
                    {
                        m.Vertices[i].UV  = new Vector2(br.ReadSingle(),
                                                        br.ReadSingle());
                    }

                }
                if (numNorms > 0)
                {
                    m.HasNormals = true;
                    if (numNorms != numVerts)
                    {
                        throw new BOBSerializerException("Normal count must match vertex length");
                    }
                    for (int i = 0; i < m.Vertices.Count; i++)
                    {
                        m.Vertices[i].Normal = new Vector3(br.ReadSingle(),
                                                           br.ReadSingle(),
                                                           br.ReadSingle());
                    }
                }
                for (int i = 0; i < numFaces; i++)
                {
                    m.Faces.Add(new Face((int)br.ReadUInt16(),
                                         (int)br.ReadUInt16(),
                                         (int)br.ReadUInt16()));
                }

                // Now read the intersection data starting with grid size
                ushort gridsize = br.ReadUInt16();
                // Read min mesh bounds
                Vector3 minBounds = new Vector3(br.ReadSingle(),
                                                br.ReadSingle(),
                                                br.ReadSingle());
                // Write max mesh bounds
                Vector3 maxBounds = new Vector3(br.ReadSingle(),
                                                br.ReadSingle(),
                                                br.ReadSingle());
                // Validate our bounds
                BoundingBox meshBounds = m.Bounds();
                if (!Vector3.AlmostEqual(meshBounds.Min, minBounds) ||
                    !Vector3.AlmostEqual(meshBounds.Max, maxBounds))
                {
                    throw new BOBSerializerException("Bob mesh bounds don't match writen values");
                }
                // Read total number of buckets
                int totalBuckets = br.ReadUInt16();
                if (totalBuckets != gridsize * gridsize * gridsize)
                {
                    throw new BOBSerializerException("Inconsistent buckets and grid size in Bob file");
                }
                // Read the size of each bucket and init array
                // Note that number of buckets should always be zero so we don't need to read them
            }
            return m;
        }

        public override void Save(Mesh m, string filename, string imageFilename)
        {
            if (m.Vertices.Count > ushort.MaxValue)
            {
                throw new BOBSerializerException("Mesh exceeds max vertex, uv, or normal count of bob file format");
            }
            using (BinaryWriter bw = new BinaryWriter(File.Open(filename, FileMode.Create)))
            {
                bw.Write((ushort)VERSION_NUM);
                bw.Write((ushort)m.Vertices.Count);
                
                bw.Write((ushort) (m.HasUVs ? m.Vertices.Count : 0));
                bw.Write((ushort) (m.HasNormals ? m.Vertices.Count : 0));
                bw.Write((ushort) m.Faces.Count);
                foreach (Vertex v in m.Vertices)
                {
                    bw.Write((float)v.Position.X);
                    bw.Write((float)v.Position.Y);
                    bw.Write((float)v.Position.Z);
                }
                if (m.HasUVs)
                {
                    foreach (Vertex v in m.Vertices)
                    {
                        bw.Write((float)v.UV.U);
                        bw.Write((float)v.UV.V);
                    }
                }
                if (m.HasNormals)
                {
                    foreach (Vertex v in m.Vertices)
                    {
                        bw.Write((float)v.Normal.X);
                        bw.Write((float)v.Normal.Y);
                        bw.Write((float)v.Normal.Z);
                    }
                }
                foreach (Face f in m.Faces)
                {
                    if (!f.IsValid())
                    {
                        throw new BOBSerializerException("Invalid face");
                    }
                    bw.Write((ushort)f.P0);
                    bw.Write((ushort)f.P1);
                    bw.Write((ushort)f.P2);
                }
                // Now write the intersection data starting with grid size
                bw.Write((ushort)GRID_SIZE);
                BoundingBox meshBB = m.Bounds();
                // Write min m bounds
                bw.Write((float)meshBB.Min.X);
                bw.Write((float)meshBB.Min.Y);
                bw.Write((float)meshBB.Min.Z);
                // Write max m bounds
                bw.Write((float)meshBB.Max.X);
                bw.Write((float)meshBB.Max.Y);
                bw.Write((float)meshBB.Max.Z);
                // Write total number of buckets
                bw.Write((ushort)(GRID_SIZE * GRID_SIZE * GRID_SIZE));
                // Buckets are depricated and line above will always write zero;
            }
        }
    }
}
