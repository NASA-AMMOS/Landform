using System;
using System.Collections.Generic;
using System.IO;

namespace JPLOPS.Geometry
{
    public class PLYSerializerException : MeshSerializerException
    {
        public PLYSerializerException() { }
        public PLYSerializerException(string message) : base(message) { }
        public PLYSerializerException(string message, Exception inner) : base(message, inner) { }
    }

    public class PLYSerializer : MeshSerializer
    {

        public static string TextureFileCommentName = "TextureFile";

        public static Mesh Read(string filename)
        {
            return Read(filename, out string textureFilename);
        }

        /// <summary>
        /// Reads and returns a mesh for a binary or ASCII ply file
        /// </summary>
        /// <param name="filename">mesh filename</param>
        /// <param name="textureFilename">the name of the meshes texture if there is one, null otherwise</param>
        /// <param name="defaultAlpha">if the ply file only has RGB then use this value as the default alpha</param>
        /// <returns>A mesh containing the ply file contents</returns>
        public static Mesh Read(string filename, out string textureFilename, double defaultAlpha = 1,
                                bool onlyGetImageFilename = false, bool readValuesAsNormalLengths = false)
        {
            var reader = new PLYReader(filename, readValuesAsNormalLengths);
            return reader.Read(out textureFilename, defaultAlpha, onlyGetImageFilename);
        }

        public static Mesh Read(string filename, double defaultAlpha = 1, bool readValuesAsNormalLengths = false)
        {
            return Read(filename, out string textureFilename, defaultAlpha, false, readValuesAsNormalLengths);
        }

        /// <summary>
        /// Write a ply file using the Maximum Compatibility PLYWriter
        /// </summary>
        /// <param name="m">The mesh to export.  Assumes vertex color values range 0-1.  Will scale these to 0-255 if
        /// byte based color proprties are chosen</param>
        /// <param name="filename">Output filename</param>
        /// <param name="comments"></param>
        public static void Write(Mesh m, string filename, string textureFilename = null, List<string> comments = null)
        {
            Write(m, filename, new PLYMaximumCompatibilityWriter(), textureFilename, comments);
        }

        /// <summary>
        /// Write a ply file
        /// </summary>
        /// <param name="m">The mesh to export.  Assumes vertex color values range 0-1.  Will scale these to 0-255 if
        /// byte based color proprties are chosen</param>
        /// <param name="filename"></param>
        /// <param name="plyWriter">The type of PLYWriter to use</param>
        /// <param name="comments"></param>
        public static void Write(Mesh m, string filename, PLYWriter plyWriter, string textureFilename = null,
                                 List<string> comments = null)
        {      
            using (StreamWriter sw = new StreamWriter(filename, false))
            {
                sw.NewLine = "\n";
                plyWriter.WriteHeader(m, sw, textureFilename, comments);
            }            
            var output = new FileStream(filename, FileMode.Append, FileAccess.Write);  
            for (int i = 0; i < m.Vertices.Count; i++)
            {
                plyWriter.WriteVertex(m, m.Vertices[i], output);
            }
            for (int i = 0; i < m.Faces.Count; i++)
            {
                plyWriter.WriteFace(m, m.Faces[i], output);
            }
            output.Close();
        }

        public override Mesh Load(string filename)
        {
            return PLYSerializer.Read(filename, out string imageFilename);
        }

        public override Mesh Load(string filename, out string imageFilename, bool onlyGetImageFilename = false)
        {
            return PLYSerializer.Read(filename, out imageFilename, onlyGetImageFilename: onlyGetImageFilename);
        }

        public override List<Mesh> LoadAllLODs(string filename)
        {
            return LoadAllLODs(filename, out string imageFilename);
        }

        public override List<Mesh> LoadAllLODs(string filename, out string imageFilename,
                                               bool onlyGetImageFilename = false)
        {
            var mesh = Load(filename, out imageFilename, onlyGetImageFilename);
            return onlyGetImageFilename ? null : new List<Mesh>() { mesh };
        }

        public override void Save(Mesh m, string filename, string imageFilename)
        {
            PLYSerializer.Write(m, filename, imageFilename);
        }

        public override string GetExtension()
        {
            return ".ply";
        }
    }
}
