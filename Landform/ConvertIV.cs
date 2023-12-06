using System;
using System.IO;
using System.Linq;
using CommandLine;
using log4net;
using JPLOPS.Util;
using JPLOPS.Geometry;
using JPLOPS.Pipeline;

/// <summary>
/// Utility to convert IV meshes to other formats.
///
/// Can operate on a single IV or a directory containing multiple IV files.
///
/// Can convert only the finest LOD or all LODs.
///
/// If converting a single IV and --texture names a file, then that is used as the texture of the output mesh.
///
/// If converting a directory --texture can give a file extension (with or without leading dot).  For each IV if
/// there is a corresponding file with the same base name but the indicated extension, that is used as the mesh texture.
///
/// Also see ConvertPDS.  If you have a directory of pairs *RASL*.iv / *RASL*.IMG you can run convert-pds first to
/// convert the IMG files to png, and then convert-iv will use those to texture the converted meshes.
///
/// Example:
///
///  LandformUtil.exe convert-pds out/windjana/meshes
///  LandformUtil.exe convert-iv out/windjana/meshes --alllods
/// </summary>
namespace JPLOPS.Landform
{
    [Verb("convert-iv", HelpText = "Convert IV meshes to different format")]
    public class ConvertIVOptions
    {
        [Value(0, Required = true, HelpText = "Path to file or directory to be converted")]
        public string InputPath { get; set; }

        [Option(Default = "png", HelpText = "Texture image file or extension")]
        public string Texture { get; set; }

        [Option(HelpText = "Convert all LODs")]
        public bool AllLODs { get; set; }

        [Option(HelpText = "Output directory, omit to use same directory as input")]
        public string OutputPath { get; set; }

        [Option(Default = "ply", HelpText = "Output file type (ply, obj)")]
        public string OutputType { get; set; }

        [Option(Default = false, HelpText = "Ignore embedded texture filename, if any")]
        public bool IgnoreEmbeddedTextureFilename { get; set; }

        [Option(Default = false, HelpText = "Don't use alternate format of embedded texture")]
        public bool StrictEmbeddedTextureFormat { get; set; }

        [Option(Default = false, HelpText = "Disable texture fallback if embedded texture missing or not found")]
        public bool NoEmbeddedTextureFallback { get; set; }

        [Option(Required = false, HelpText = "Don't clean mesh")]
        public bool NoClean { get; set; }
    }

    public class ConvertIV
    {
        private ConvertIVOptions options;

        private static readonly ILog logger = LogManager.GetLogger(typeof(ConvertIV));

        public ConvertIV(ConvertIVOptions options)
        {
            this.options = options;
        }

        public int Run()
        {
            try
            {
                string[] allowedFormats = new string[] { "ply", "obj" };
                
                if (!allowedFormats.Any(f => f == options.OutputType))
                {
                    logger.ErrorFormat("unrecognized output type \"{0}\"", options.OutputType);
                    return 1;
                }
                
                string[] files = null;
                string destDir = null;
                
                bool directoryMode = Directory.Exists(options.InputPath);
                
                if (directoryMode)
                {
                    files = Directory.GetFiles(options.InputPath, "*.iv");
                    destDir = options.InputPath;
                }
                else
                {
                    files = new string[] {  options.InputPath };
                    destDir = Path.GetDirectoryName(options.InputPath); //destDir="" if InputPath was a bare filename
                }
                
                if (options.OutputPath != null)
                {
                    destDir = options.OutputPath;
                }
                
                if (files != null && files.Length > 0)
                {
                    
                    if (!string.IsNullOrEmpty(destDir))
                    {
                        Directory.CreateDirectory(destDir);
                    }
                    
                    string ext = "." + options.OutputType;
                    
                    string tf = options.Texture;
                    string tfExt = Path.GetExtension(tf);
                    if (string.IsNullOrEmpty(tfExt))
                    {
                        tfExt = tf;
                    }
                    if (!string.IsNullOrEmpty(tfExt))
                    {
                        tfExt = tfExt.TrimStart('.');
                        tfExt = "." + tfExt;
                    }
                    
                    for (int i = 0; i < files.Length; i++)
                    {
                        string bn = Path.GetFileNameWithoutExtension(files[i]);
                        string tft = tf;
                        if (!string.IsNullOrEmpty(tfExt) && directoryMode)
                        {
                            tft = Path.ChangeExtension(files[i], tfExt);
                            if (!File.Exists(tft))
                            {
                                tft = tf;
                            }
                        }
                        //see comments in ProcessTactical.cs AddImage()
                        string dir = Path.GetDirectoryName(files[i]);
                        var id = RoverProductId.Parse(bn, throwOnFail: false);
                        if (id != null)
                        {
                            foreach (string tryId in id.DescendingVersions(10))
                            {
                                tft = Path.Combine(dir, tryId + tfExt);
                                if (File.Exists(tft))
                                {
                                    break;
                                }
                            }
                        }
                        if (!File.Exists(tft))
                        {
                            tft = null;
                        }
                        if (tft != null)
                        {
                            tft = Path.GetFileName(tft);
                        }
                        string dirMsg = destDir != null && destDir != "" && destDir != "." ? (" in " + destDir) : "";
                        string handleEmbeddedTexureFilename(string tfe)
                        {
                            if (tfe == null)
                            {
                                logger.InfoFormat("no embedded texture filename");
                                if (!options.NoEmbeddedTextureFallback)
                                {
                                    logger.InfoFormat("using fallback texture");
                                    return tft;
                                }
                                return null;
                            }
                            logger.InfoFormat("embedded texture filename " + tfe);
                            if (options.IgnoreEmbeddedTextureFilename)
                            {
                                logger.InfoFormat("ignoring embedded texture filename");
                                return tft;
                            }
                            string atfe = (tfExt != null && !tfe.EndsWith(tfExt) &&
                                           (tf == tfExt || ("." + tf) == tfExt)) ?
                                Path.ChangeExtension(tfe, tfExt) : null;
                            if (!options.StrictEmbeddedTextureFormat && atfe != null &&
                                File.Exists(Path.Combine(dir, atfe)))
                            {
                                logger.InfoFormat("using {0} format texture instead of {1}",
                                                  tfExt, Path.GetExtension(tfe));
                                return atfe;
                            }
                            if (File.Exists(Path.Combine(dir, tfe)))
                            {
                                logger.InfoFormat("using embeded texture filename");
                                return tfe;
                            }
                            logger.InfoFormat("embeded texture file not found");
                            if (!options.NoEmbeddedTextureFallback && tft != null)
                            {
                                logger.InfoFormat("using fallback texture");
                                return tft;
                            }
                            return null;
                        }
                        if (options.AllLODs)
                        {
                            var lodMeshes = Mesh.LoadAllLODs(files[i], out string tfe);
                            logger.InfoFormat("converting {0} LOD from {1} to {2}{3}",
                                              lodMeshes.Count, files[i], ext, dirMsg);
                            tft = handleEmbeddedTexureFilename(tfe);
                            logger.InfoFormat("texture file {0}", tft != null ? tft : "(not found)");
                            for (int lod = 0; lod < lodMeshes.Count; lod++)
                            {
                                LoadedMesh(lodMeshes[lod]);
                                if (!options.NoClean)
                                {
                                    CleanMesh(lodMeshes[lod]);
                                }
                                string dest = string.Format("{0}_LOD{1:00}{2}", bn, lod, ext);
                                logger.InfoFormat("saving {0} ({1} tris)", dest, Fmt.KMG(lodMeshes[lod].Faces.Count));
                                lodMeshes[lod].Save(Path.Combine(destDir, dest), tft); //destDir="" ok
                            }
                        }
                        else
                        {
                            var mesh = Mesh.Load(files[i], out string tfe);
                            logger.InfoFormat("converting {0} to {1}{2}", files[i], ext, dirMsg);
                            tft = handleEmbeddedTexureFilename(tfe);
                            logger.InfoFormat("texture file {0}", tft != null ? tft : "(not found)");
                            LoadedMesh(mesh);
                            if (!options.NoClean)
                            {
                                CleanMesh(mesh);
                            }
                            mesh.Save(Path.Combine(destDir, bn + ext), tft); //destDir="" ok
                        }
                    }          
                }
            }
            catch (Exception ex)
            {
                Logging.LogException(logger, ex);
                return 1;
            }

            return 0;
        }

        private void LoadedMesh(Mesh mesh)
        {
            logger.InfoFormat("loaded mesh with {0} vertices, {1} faces, {2} normals, {3} colors, {4} texcoords",
                              mesh.Vertices.Count, mesh.Faces.Count, mesh.HasNormals ? "with" : "without",
                              mesh.HasColors ? "with" : "without", mesh.HasUVs ? "with" : "without");
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
