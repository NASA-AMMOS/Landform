using CommandLine;
using log4net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Cloud;
using OPS.Util;
using System.IO;
using OPS.Pipeline.TileServer;
using OPS.Geometry;

namespace OPS.Pipeline
{

    [Verb("emttoscene", HelpText = "Convert emt data into an ASTTRO scene")]
    public class EmtToSceneOptions
    {
        [Value(0, Required = true, HelpText = "Input iv directory path or S3 location")]
        public string InputIVFile { get; set; }

        [Value(1, Required = true, HelpText = "Project name for tiling server")]
        public string TilingProjectName { get; set; }
        
        [Option(Required = false, Default = "menzies_m2020_gov", HelpText = "")]
        public string InputAWSProfile { get; set; }

        [Option(Required = false, Default = "us-gov-west-1", HelpText = "")]
        public string InputAWSRegion { get; set; }
        
        [Option(Required = false, Default = null, HelpText = "")]
        public string WorkingDir { get; set; }



    }

    public class EmtToScene
    {
        EmtToSceneOptions options;

        private static readonly ILog logger = LogManager.GetLogger(typeof(EmtToScene));


        public EmtToScene(EmtToSceneOptions opts)
        {
            options = opts;
        }


        string GetFile(string location)
        {
            var path = Path.Combine(options.WorkingDir, Path.GetFileName(location));
            if (!File.Exists(path))
            {
                if (location.StartsWith("s3://"))
                {
                    Console.WriteLine("location: " + location);
                    var inputStorageHelper = new StorageHelper(options.InputAWSProfile, options.InputAWSRegion);
                    inputStorageHelper.DownloadFile(location, path);
                }
                else
                {
                    File.Copy(location, path);
                }
            }
            return path;
        }


        public List<string> GetMeshLocationList()
        {
            if (options.InputIVFile.EndsWith(".iv"))
            {
                // Use the files listed in the master iv file
                var lines = File.ReadAllLines(GetFile(options.InputIVFile));
                List<string> results = new List<string>();
                foreach (var line in lines)
                {
                    if (!line.Contains(".iv"))
                    {
                        continue;
                    }
                    int start = line.IndexOf("\"") + 1;
                    int end = line.LastIndexOf("\"");
                    var relativeFile = line.Substring(start, end - start);
                    if (relativeFile.StartsWith("."))
                    {
                        relativeFile = relativeFile.Substring(1, relativeFile.Length - 1);
                    }
                    string absFile;
                    if (options.InputIVFile.StartsWith("s3://"))
                    {
                        var root = S3GetDirectory(options.InputIVFile);
                        absFile = root + relativeFile;
                    }
                    else
                    {
                        absFile = Path.Combine(Path.GetDirectoryName(options.InputIVFile), relativeFile);
                    }

                    results.Add(absFile);
                }
                return results;
            }
            else
            {
                // Assume the input is a directory and use all iv files in it
                var inputStorageHelper = new StorageHelper(options.InputAWSProfile, options.InputAWSRegion);
                var results = inputStorageHelper.SearchObjects(options.InputIVFile, "*.iv").ToList();
                return results;
            }
        }


        class MeshRecord
        {
            public string IV;
            public string RGB;

            public string OBJ;
            public string IMG;
            public string VIC;
            public string PNG;

            public MeshRecord(string ivLocation, EmtToScene emtToScene)
            {
                IV = emtToScene.GetFile(ivLocation);
                RGB = emtToScene.GetFile(ivLocation.Replace(".iv", ".rgb"));
                PNG = emtToScene.GetFile(ivLocation.Replace(".iv", ".png"));
                var objLocation = (ivLocation.Replace("mesh/wedge2", "ncam")).Replace(".iv", ".obj");
                Console.WriteLine("objLocation: " + objLocation);
                OBJ = emtToScene.GetFile(objLocation);
                IMG = emtToScene.GetFile(objLocation.Replace(".obj", ".IMG"));
                VIC = emtToScene.GetFile(objLocation.Replace(".obj", ".VIC"));
                
            }
        }

        public int Run()
        {

            if (options.WorkingDir == null)
            {
                options.WorkingDir = TemporaryFile.GetTempDirectory();
            }

            int r;
            var meshLocations = GetMeshLocationList();
            var records = meshLocations.Select(f => new MeshRecord(f, this)).ToList();
            // Filter empty meshes
            records = records.Where(rec =>
            {
                var m = Mesh.Load(rec.OBJ);
                return m.Vertices.Count > 0 && m.Faces.Count > 0;
            }).ToList();
            LegacySceneManfiest.BuildFromDirectory(options.WorkingDir);
            var createOptions = new CreateProjectOptions()
            {
                ProjectName = options.TilingProjectName,
                TilingScheme = TilingScheme.QuadY,
                SkirtMode = Geometry.SkirtMode.None,
                ReconMethod = Geometry.MeshReconMethod.Poisson,
                FacesPerTile = 2000,
                TileResolution = 256,
                ProjectType = PipelineStateMachine.ProjectType.GenericTiling,
                NoWait = false
            };

            r = new CreateProject(createOptions).Run();

            foreach (var f in Directory.EnumerateFiles(Path.Combine(options.WorkingDir, "preprocessed"), "*.obj"))
            {
                string img = f.Replace(".obj", ".jpg");
                var uploadOptions = new UploadInputOptions()
                {
                    ProjectName = options.TilingProjectName,
                    MeshFilepath = f,
                    ImageFilepath = img,
                    TileId = null,
                    NoWait = false
                };
                r = new UploadInput(uploadOptions).Run();
            }

            var runOptions = new RunProjectOptions()
            {
                ProjectName = options.TilingProjectName
            };
            r = new RunProject(runOptions).Run();

            var tilesetUrl = TileServerConfig.Instance.WWWUrl(options.TilingProjectName);
            logger.Info("Building tileset.  When done copy data from " + tilesetUrl + " to tile3d_2.0 directory");
            return 0;
        }

        public string S3GetDirectory(string path)
        {
            return path.Substring(0, path.LastIndexOf("/"));
        }

        class CopyDir
        {
            public static void Copy(string sourceDirectory, string targetDirectory)
            {
                DirectoryInfo diSource = new DirectoryInfo(sourceDirectory);
                DirectoryInfo diTarget = new DirectoryInfo(targetDirectory);

                CopyAll(diSource, diTarget);
            }

            public static void CopyAll(DirectoryInfo source, DirectoryInfo target)
            {
                Directory.CreateDirectory(target.FullName);

                // Copy each file into the new directory.
                foreach (FileInfo fi in source.GetFiles())
                {
                    fi.CopyTo(Path.Combine(target.FullName, fi.Name), true);
                }

                // Copy each subdirectory using recursion.
                foreach (DirectoryInfo diSourceSubDir in source.GetDirectories())
                {
                    DirectoryInfo nextTargetSubDir =
                        target.CreateSubdirectory(diSourceSubDir.Name);
                    CopyAll(diSourceSubDir, nextTargetSubDir);
                }
            }
        }
    }
}
