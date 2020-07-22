using System;
using System.IO;
using System.Linq;
using CommandLine;
using log4net;
using OPS.Util;
using OPS.Imaging;
using OPS.Geometry;
using OPS.Pipeline;

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
namespace OPS.Landform
{
    [Verb("convert-iv", HelpText = "Convert IV meshes to different format")]
    public class ConvertIVOptions
    {
        [Value(0, Required = true, HelpText = "Path to file or directory to be converted")]
        public string InputPath { get; set; }

        [Option(Required = false, Default = "png", HelpText = "Texture image file or extension")]
        public string Texture { get; set; }

        [Option(Required = false, HelpText = "Convert all LODs")]
        public bool AllLODs { get; set; }

        [Option(Required = false, HelpText = "Output directory, omit to use same directory as input")]
        public string OutputPath { get; set; }

        [Option(Required = false, Default = "ply", HelpText = "Output file type (ply, obj)")]
        public string OutputType { get; set; }
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
                        //TODO https://github.jpl.nasa.gov/OnSight/Landform/issues/951
                        //see comments in ProcessTactical.cs AddImage()
                        var id = RoverProductId.Parse(bn, throwOnFail: false);
                        if (id != null)
                        {
                            string dir = Path.GetDirectoryName(files[i]);
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
                        if (options.AllLODs)
                        {
                            var lodMeshes = Mesh.LoadAllLODs(files[i]);
                            logger.InfoFormat("converting {0} LOD from {1} to {2} in {3}",
                                              lodMeshes.Count, files[i], ext, destDir);
                            for (int lod = 0; lod < lodMeshes.Count; lod++)
                            {
                                string dest = string.Format("{0}_LOD{1}{2}", bn, lod, ext);
                                lodMeshes[lod].Save(Path.Combine(destDir, dest), tft); //destDir="" ok
                            }
                        }
                        else
                        {
                            logger.InfoFormat("converting {0} to {1} in {2}", files[i], ext, destDir);
                            Mesh.Load(files[i]).Save(Path.Combine(destDir, bn + ext), tft); //destDir="" ok
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
    }
}
