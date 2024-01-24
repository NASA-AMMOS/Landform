using System;
using System.IO;
using System.Linq;
using CommandLine;
using log4net;
using JPLOPS.Util;
using JPLOPS.Geometry;
using JPLOPS.Geometry.GLTF;

/// <summary>
/// Utility to convert meshes to/from glTF, GLB, and B3DM. 
///
/// Can operate on a single mesh or a directory containing multiple glTF/GLB/B3DM files.
///
/// If converting one mesh to glTF/BLB/B3DM and --texture names a file, then that is used as the texture.
///
/// Example:
///
///  LandformUtil.exe convert-gltf out/windjana/tilesets/0630_0311472
///  LandformUtil.exe convert-gltf out/windjana/tilesets/0630_0311472/0.b3dm
///  LandformUtil.exe convert-gltf foo.ply --texture foo.jpg --outputpath foo.b3dm
/// </summary>
namespace JPLOPS.Landform
{
    [Verb("convert-gltf", HelpText = "Convert IV meshes to different format")]
    public class ConvertGLTFOptions
    {
        [Value(0, Required = true, HelpText = "Path to file or directory to be converted")]
        public string InputPath { get; set; }

        [Option(Required = false, HelpText = "Texture image file")]
        public string Texture { get; set; }

        [Option(Required = false, HelpText = "Index image file")]
        public string Index { get; set; }

        [Option(Required = false, HelpText = "Output file or directory, omit to match input")]
        public string OutputPath { get; set; }

        [Option(Required = false, HelpText = "Output file type (ply, obj, gltf, glb, b3dm)")]
        public string OutputType { get; set; }

        [Option(Required = false, HelpText = "Don't clean mesh")]
        public bool NoClean { get; set; }
    }

    public class ConvertGLTF
    {
        private ConvertGLTFOptions options;

        private static readonly ILog logger = LogManager.GetLogger(typeof(ConvertGLTF));

        public ConvertGLTF(ConvertGLTFOptions options)
        {
            this.options = options;
        }

        public int Run()
        {
            try
            {
                if (Directory.Exists(options.InputPath))
                {
                    var destDir = !string.IsNullOrEmpty(options.OutputPath) ? options.OutputPath : options.InputPath;
                    if (!Directory.Exists(destDir))
                    {
                        Directory.CreateDirectory(destDir);
                    }
                    foreach (var file in Directory.GetFiles(options.InputPath))
                    {
                        if (IsGLTF(file))
                        {
                            ConvertFromGLTF(file, destDir);
                        }
                    }
                }
                else if (IsGLTF(options.InputPath))
                {
                    string destFileOrDir = options.OutputPath;
                    if (string.IsNullOrEmpty(destFileOrDir))
                    {
                        destFileOrDir = Path.GetDirectoryName(options.InputPath); //"" if InputPath was only filename
                    }
                    ConvertFromGLTF(options.InputPath, destFileOrDir);
                }
                else
                {
                    string gltfFile = options.OutputPath, gltfType = "auto";
                    if (string.IsNullOrEmpty(gltfFile))
                    {
                        gltfFile = Path.ChangeExtension(options.InputPath, gltfType);
                    }
                    else
                    {
                        gltfType = Path.GetExtension(gltfFile);
                    }
                    if (!string.IsNullOrEmpty(options.OutputType))
                    {
                        gltfType = options.OutputType.ToLower();
                    }
                    if (string.IsNullOrEmpty(gltfType) || gltfType == "auto")
                    {
                        gltfType = "gltf";
                    }
                    if (!IsGLTF(gltfType))
                    {
                        throw new Exception("unsupported glTF type: " + gltfType);
                    }
                    gltfFile = Path.ChangeExtension(gltfFile, gltfType);
                    ConvertToGLTF(options.InputPath, options.Texture, options.Index, gltfFile);
                }
            }
            catch (Exception ex)
            {
                Logging.LogException(logger, ex);
                return 1;
            }
            return 0;
        }

        private bool IsGLTF(string fileOrExt)
        {
            if (fileOrExt.IndexOf('.') > 0)
            {
                fileOrExt = Path.GetExtension(fileOrExt);
            }
            string ext = fileOrExt.ToLower().TrimStart('.');
            string[] gltfFormats = new string[] { "gltf", "glb", "b3dm" };
            return gltfFormats.Any(f => f == ext);
        }

        private void ConvertFromGLTF(string gltfFile, string destFileOrDir)
        {
            string destType = Path.GetExtension(destFileOrDir);
            if (!string.IsNullOrEmpty(options.OutputType))
            {
                destType = options.OutputType.ToLower();
            }
            if (string.IsNullOrEmpty(destType) || destType == "auto")
            {
                destType = "ply";
            }
            string[] allowedFormats = new string[] { "ply", "obj", "gltf", "glb", "b3dm" };
            if (!allowedFormats.Any(f => f == destType))
            {
                throw new Exception("unsupported output type: " + destType);
            }

            string destFile = destFileOrDir;
            if (string.IsNullOrEmpty(destFile))
            {
                destFile = Path.ChangeExtension(gltfFile, destType);
            }

            string basename =
                Path.GetFileNameWithoutExtension(!string.IsNullOrEmpty(destFileOrDir) ? destFileOrDir : gltfFile);

            if (Directory.Exists(destFileOrDir))
            {
                basename = Path.GetFileNameWithoutExtension(gltfFile);
                destFile = Path.Combine(destFileOrDir, $"{basename}.{destType}");
            }

            string destTexture = null, destIndex = null;
            GLTFFile.ImageHandler imageHandler = (mimeType, bytes) =>
            {
                string ext = GLTFFile.MimeToExt(mimeType);
                destTexture = Path.Combine(Path.GetDirectoryName(destFile), $"{basename}.{ext}");
                logger.InfoFormat("converting texture image from {0} to {1}", gltfFile, destTexture);
                File.WriteAllBytes(destTexture, bytes);
            };
            GLTFFile.ImageHandler indexHandler = (mimeType, bytes) =>
            {
                string ext = GLTFFile.MimeToExt(mimeType);
                destIndex = Path.Combine(Path.GetDirectoryName(destFile), $"{basename}_index.{ext}");
                logger.InfoFormat("converting index image from {0} to {1}", gltfFile, destIndex);
                File.WriteAllBytes(destIndex, bytes);
            };
            Mesh mesh = null;
            string gltfType = Path.GetExtension(gltfFile).ToLower().TrimStart('.');
            switch (gltfType)
            {
                case "gltf": mesh = GLTFSerializer.Load(gltfFile, imageHandler, indexHandler); break;
                case "glb": mesh = GLBSerializer.Load(gltfFile, imageHandler, indexHandler); break;
                case "b3dm": mesh = B3DMSerializer.Load(gltfFile, imageHandler, indexHandler); break;
                default: throw new Exception("unsupported glTF type: " + gltfType);
            }
            logger.InfoFormat("loaded mesh with {0} vertices, {1} faces, {2} normals, {3} colors, {4} texcoords",
                              mesh.Vertices.Count, mesh.Faces.Count, mesh.HasNormals ? "with" : "without",
                              mesh.HasColors ? "with" : "without", mesh.HasUVs ? "with" : "without");
            if (!options.NoClean)
            {
                CleanMesh(mesh);
            }
            logger.InfoFormat("converting {0} to {1}", gltfFile, destFile);
            mesh.Save(destFile, destTexture);
        }

        private void ConvertToGLTF(string meshFile, string textureFile, string indexFile, string gltfFile)
        {
            logger.InfoFormat("converting {0}{1}{2} to {3}",
                              meshFile,
                              textureFile != null ? (", " + textureFile) : "",
                              indexFile != null ? (", index " + indexFile) : "",
                              gltfFile);
            var mesh = Mesh.Load(meshFile);
            CleanMesh(mesh);
            string gltfType = Path.GetExtension(gltfFile).ToLower().TrimStart('.');
            switch (gltfType)
            {
                case "gltf": GLTFSerializer.Save(mesh, gltfFile, textureFile, indexFile); break;
                case "glb": GLBSerializer.Save(mesh, gltfFile, textureFile, indexFile); break;
                case "b3dm": B3DMSerializer.Save(mesh, gltfFile, textureFile, indexFile); break;
                default: throw new Exception("unsupported glTF type: " + gltfType);
            }
        }

        private void CleanMesh(Mesh mesh)
        {
            mesh.Clean(verbose: msg => logger.Info(msg), warn: msg => logger.Warn(msg));
            logger.InfoFormat("cleaned mesh has {0} vertices, {1} faces, {2} normals, {3} colors, {4} texcoords",
                              mesh.Vertices.Count, mesh.Faces.Count, mesh.HasNormals ? "with" : "without",
                              mesh.HasColors ? "with" : "without", mesh.HasUVs ? "with" : "without");
        }
    }
}
