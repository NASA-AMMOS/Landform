using CommandLine;
using log4net;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Cloud;
using OPS.Util;
using System.IO;
using OPS.Pipeline.TileServer;
using OPS.Geometry;
using OPS.Imaging;
using Microsoft.Xna.Framework;

namespace OPS.Pipeline
{

    [Verb("emttoscene", HelpText = "Convert emt data into an ASTTRO scene")]
    public class EmtToSceneOptions
    {
        [Value(0, Required = true, HelpText = "Tiling project name")]
        public string ProjectName { get; set; }

        [Value(1, Required = true, HelpText = "List of S3 locations to search for data")]
        public IEnumerable<string> SearchLocations { get; set; }
        
        [Option(Required = true, HelpText = "")]
        public string InputAWSProfile { get; set; }

        [Option(Required = false, Default = "us-gov-west-1", HelpText = "")]
        public string InputAWSRegion { get; set; }
        
        [Option(Required = false, Default = null, HelpText = "")]
        public string WorkingDir { get; set; }

        [Option(Required = false, Default = null, HelpText = "If set, meshes will be decimated by this amount before tiling.  Valid range [0-1]")]
        public double? DecimationRatio { get; set; }

        [Option(Required = false, Default = null, HelpText = "If set, this file can be used to filter what products IDs should be used.  Each line should contain a product ID to include, all others will be excluded")]
        public string MeshInclude { get; set; }

        [Option(Required = false, Default = null, HelpText = "If set, this file can be used to filter what products IDs should be used.  Each line should contain a product ID to exclude, all others will be included")]
        public string MeshExclude { get; set; }

        [Option(Required = false, Default = null, HelpText = "If set, this file can be used to filter what products IDs should be used.  Each line should contain a product ID to include, all others will be excluded")]
        public string ImageInclude { get; set; }

        [Option(Required = false, Default = null, HelpText = "If set, this file can be used to filter what products IDs should be used.  Each line should contain a product ID to exclude, all others will be included")]
        public string ImageExclude { get; set; }

        [Option(Required = false, Default = null, HelpText = "Should mastcam meshes be used")]
        public bool MeshMastcam { get; set; }

        [Option(Required = false, Default = null, HelpText = "Should hazcam meshes be used")]
        public bool MeshHazcam{ get; set; }
        
        [Option(Required = false, Default = 16, HelpText = "Control the number of concurrent downloads")]
        public int ConcurrentDownloads { get; set; }

        [Option(Required = false, Default = 4, HelpText = "Control the number of concurrent mesh operations")]
        public int ConcurrentMeshOps { get; set; }

        [Option(Required = false, Default = false, HelpText = "Start a tiling server within this process")]
        public bool RunTilingServer { get; set; }

        [Option(Required = false, Default = false, HelpText = "Force recalculating normals using meshlab at the begining")]
        public bool ForceNormalComputation { get; set; }
    }

    public class EmtToScene
    {
        EmtToSceneOptions options;

        private static readonly ILog logger = LogManager.GetLogger(typeof(EmtToScene));

        public class FileRecord
        {
            public string FilenameBase { get; private set; }
            public string IV, OBJ, MTL, IMG, VIC, PNG, RGB, JPG;

            public static string GetFilenameBase(string filename)
            {
                return Path.GetFileNameWithoutExtension(filename);
            }

            public FileRecord(string filename)
            {
                this.FilenameBase = GetFilenameBase(filename);
                AddFile(filename);
            }

            public FileRecord(FileRecord other)
            {
                this.FilenameBase = other.FilenameBase;
                this.IV = other.IV;
                this.OBJ = other.OBJ;
                this.MTL = other.MTL;
                this.IMG = other.IMG;
                this.VIC = other.VIC;
                this.PNG = other.PNG;
                this.RGB = other.RGB;
                this.JPG = other.JPG;
            }

            public void ChangePath(string basePath)
            {
                if(this.IV!= null)
                {
                    this.IV = PathHelper.ChangeDirectory(this.IV, basePath);
                }
                if (this.OBJ != null)
                {
                    this.OBJ = PathHelper.ChangeDirectory(this.OBJ, basePath);
                }
                if (this.MTL != null)
                {
                    this.MTL = PathHelper.ChangeDirectory(this.MTL, basePath);
                }
                if (this.IMG != null)
                {
                    this.IMG = PathHelper.ChangeDirectory(this.IMG, basePath);
                }
                if (this.VIC != null)
                {
                    this.VIC = PathHelper.ChangeDirectory(this.VIC, basePath);
                }
                if (this.PNG != null)
                {
                    this.PNG = PathHelper.ChangeDirectory(this.PNG, basePath);
                }
                if (this.RGB != null)
                {
                    this.RGB = PathHelper.ChangeDirectory(this.RGB, basePath);
                }
                if (this.JPG != null)
                {
                    this.JPG = PathHelper.ChangeDirectory(this.JPG, basePath);
                }
            }

            public bool Nav
            {
                get
                {
                    return FilenameBase.StartsWith("N");
                }
            }

            public bool Mast
            {
                get
                {
                    return FilenameBase.StartsWith("M");
                }
            }

            public bool Haz
            {
                get
                {
                    return FilenameBase.StartsWith("F") || FilenameBase.StartsWith("R");
                }
            }

            public bool RASL
            {
                get
                {
                    return this.FilenameBase.Contains("RASL");
                }
            }

            public bool RAS
            {
                get
                {
                    return this.FilenameBase.Contains("RAS_");
                }
            }

            public string RASLBaseName
            {
                get
                {
                    return this.FilenameBase.Replace("RAS_", "RASL");
                }
            }

            public string RASBaseName
            {
                get
                {
                    return this.FilenameBase.Replace("RASL", "RAS_");
                }
            }

            public bool Thumbnail
            {
                get
                {
                    return this.FilenameBase.Contains("RASLT") || this.FilenameBase.Contains("RAS_T");
                }
            }

            public bool IsLeft
            {
                get
                {
                    return this.FilenameBase[1] == 'L';
                }
            }

            public string PreferedMesh
            {
                get
                {
                    if(this.OBJ != null)
                    {
                        return this.OBJ;
                    }
                    else if (this.IV != null)
                    {
                        return this.IV;
                    }
                    return null;
                }
            }

            public string PreferedImage
            {
                get
                {
                    if (this.PNG != null)
                    {
                        return this.PNG;
                    }
                    else if (this.RGB != null)
                    {
                        return this.RGB;
                    }
                    else if (this.IMG != null)
                    {
                        return this.IMG;
                    }
                    else if (this.VIC != null)
                    {
                        return this.VIC;
                    }
                    else if (this.JPG != null)
                    {
                        return this.JPG;
                    }
                    return null;
                }
            }

            public string PreferedMetadataImage
            {
                get
                {
                    if (this.IMG != null)
                    {
                        return this.IMG;
                    }
                    else if (this.VIC != null)
                    {
                        return this.VIC;
                    }
                    return null;
                }
            }

            public bool HasImage
            {
                get
                {
                    return PreferedImage != null;
                }
            }

            public bool HasMetadata
            {
                get
                {
                    return PreferedMetadataImage != null;
                }
            }


            public bool HasMesh
            {
                get
                {
                    return PreferedMesh != null;
                }
            }

            public void AddFile(string filename)
            {
                if(GetFilenameBase(filename) != this.FilenameBase)
                {
                    throw new Exception("New file does not match records FilenameBase");
                }
                string ext = Path.GetExtension(filename).ToLower();
                switch (ext)
                {
                    case ".iv":
                        this.IV = filename;
                        break;
                    case ".obj":
                        this.OBJ = filename;
                        break;
                    case ".mtl":
                        this.MTL = filename;
                        break;
                    case ".img":
                        this.IMG = filename;
                        break;
                    case ".png":
                        this.PNG = filename;
                        break;
                    case ".rgb":
                        this.RGB = filename;
                        break;
                    case ".vic":
                        this.VIC = filename;
                        break;
                    case ".jpg":
                        this.JPG = filename;
                        break;
                    default:
                        break;
                }
            }
        }

        public IEnumerable<FileRecord> IndexFiles(IEnumerable<string> searchDirectories)
        {
            Dictionary<string, FileRecord> results = new Dictionary<string, FileRecord>();
            foreach (var searchDir in searchDirectories)
            {
                logger.Info("Searching " + searchDir);
                var inputStorageHelper = new StorageHelper(options.InputAWSProfile, options.InputAWSRegion);
                var paths = inputStorageHelper.SearchObjects(searchDir).ToList();
                foreach (var path in paths)
                {
                    var fbase = FileRecord.GetFilenameBase(path);
                    if (!results.ContainsKey(fbase))
                    {
                        results.Add(fbase, new FileRecord(path));
                    }
                    var rec = results[fbase];
                    rec.AddFile(path);
                }
            }
            return results.Values.ToList();
        }


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
        
        void DownloadFile(string s3Location, string localPath)
        {
            if(!File.Exists(localPath))
            {
                TemporaryFile.GetAndMove(localPath, f =>
                {
                    var inputStorageHelper = new StorageHelper(options.InputAWSProfile, options.InputAWSRegion);
                    inputStorageHelper.DownloadFile(s3Location, f);
                });
                logger.Info("Downloaded: " + Path.GetFileName(localPath));
            }
        }

        IEnumerable<FileRecord> DownloadAndConvertToLocal(IEnumerable<FileRecord> records, string destination, bool imagesOnly = false)
        {
            ConcurrentBag<FileRecord> results = new ConcurrentBag<FileRecord>();
            var po = new ParallelOptions() { MaxDegreeOfParallelism = options.ConcurrentDownloads};
            CoreLimitedParallel.ForEach(records, po, r => 
            {            
                var localRec = new FileRecord(r);
                localRec.ChangePath(destination);
                if (r.HasImage)
                {
                    DownloadFile(r.PreferedImage, localRec.PreferedImage);
                }
                if (r.HasMetadata)
                {
                    DownloadFile(r.PreferedMetadataImage, localRec.PreferedMetadataImage);
                }
                if (r.HasMesh && !imagesOnly)
                {
                    DownloadFile(r.PreferedMesh, localRec.PreferedMesh);
                    if(r.MTL != null && Path.GetExtension(r.PreferedMesh).ToLower() == ".obj")
                    {
                        DownloadFile(r.MTL, localRec.MTL);
                    }
                }   
                results.Add(localRec);
            });
            return results.ToList();
        }

        IEnumerable<FileRecord> ProcessMeshes(IEnumerable<FileRecord> localFileRecords, string destination, double? decimationRatio)
        {
            ConcurrentBag<FileRecord> results = new ConcurrentBag<FileRecord>();
            ConcurrentBag<FileRecord> empty = new ConcurrentBag<FileRecord>();
            var po = new ParallelOptions() { MaxDegreeOfParallelism = options.ConcurrentMeshOps };
            CoreLimitedParallel.ForEach(localFileRecords, po, localRecord =>
            {
                var processedRecord = new FileRecord(Path.Combine(destination, Path.GetFileName(localRecord.OBJ)));
                processedRecord.AddFile(Path.Combine(destination, processedRecord.FilenameBase + ".png"));
                results.Add(processedRecord);

                if(File.Exists(processedRecord.PreferedMesh) && File.Exists(processedRecord.PreferedImage))
                {
                    return;
                }
                logger.Info("Processing: " + Path.GetFileName(localRecord.PreferedMesh));
                Mesh m = Mesh.Load(localRecord.PreferedMesh);
                if(m.Faces.Count == 0)
                {
                    empty.Add(processedRecord);
                    return;
                }
                var parser = new PDSParser(new PDSMetadata(localRecord.PreferedMetadataImage));

                if (!m.HasNormals || options.ForceNormalComputation)
                {
                    logger.Info("Input mesh missing normals or force normal computation is set, generating normals");
                    m.GenerateVertexNormals();
                }
                if (decimationRatio.HasValue)
                {
                    int targetFaces = (int)(m.Faces.Count * decimationRatio.Value);
                    logger.Info("Decimating: " + m.Faces.Count + " down to " + targetFaces);
                    m = EdgeCollapse.QuadricEdgeCollapse(m, targetFaces);
                    m = BaselineAtlaser.AtlasSiteFrameMesh(m, Image.Load(localRecord.PreferedMetadataImage));                  

                }
                m.Translate(-parser.OriginOffset);
                ConvertMeshToYUp(m);
                Image img = Image.Load(localRecord.PreferedImage);
                TemporaryFile.GetAndMove(processedRecord.PreferedImage, tmp =>
                {
                    img.Save<ushort>(tmp);
                });  
                m.Save(processedRecord.PreferedMesh, processedRecord.PreferedImage);
      
            });
            //Filter empty meshes
            return results.Where(r => !empty.Contains(r)).ToList();
        }

        HashSet<string> ReadFilterFile(string path)
        {
            var lines = File.ReadAllLines(path).Select(line => line.Trim()).Where(line => !string.IsNullOrEmpty(line));
            return new HashSet<string>(lines);
        }
        
        static public string GetTilesetDir(string workingDir, string primarySiteDrive)
        {
            string sceneDir = Path.Combine(workingDir, "Scene");
            string sceneSiteDriveFolder = Path.Combine(sceneDir, Path.Combine("ds" + primarySiteDrive, "201801010000"));
            string tileDir = Path.Combine(sceneSiteDriveFolder, "tile3d_2.0");
            return StringHelper.NormalizeSlashes(tileDir,true);
        }

        static public void CreateLegacyScene(IEnumerable<FileRecord> localFileRecords, string workingDir, out string manifestPath, string primarySiteDrive = null)
        {
            string sceneDir = Path.Combine(workingDir, "Scene");
            string imagesDir = Path.Combine(sceneDir, "images");
            PathHelper.EnsureExists(imagesDir);

            ConcurrentBag<LegacySceneManfiest.ImageData> imageDatas = new ConcurrentBag<LegacySceneManfiest.ImageData>();
            CoreLimitedParallel.ForEach(localFileRecords, rec =>
            {
                var imageData = new LegacySceneManfiest.ImageData()
                {
                    FileId = rec.FilenameBase,
                    Metadata = new PDSMetadata(rec.PreferedMetadataImage)
                };
                imageDatas.Add(imageData);
            });
            imageDatas = new ConcurrentBag<LegacySceneManfiest.ImageData>(imageDatas.Where(id => new PDSParser(id.Metadata).SiteDrive != null));
            var groupedImageData = imageDatas.GroupBy(id => new PDSParser(id.Metadata).SiteDrive.ToString());

            if (primarySiteDrive == null)
            {
                primarySiteDrive = groupedImageData.Select(g => g.Key).OrderBy(x => x).Last();
            }
            logger.Info("Converting images for scene");
            CoreLimitedParallel.ForEach(localFileRecords, rec => 
            {
                string siteDrive = new PDSParser(new PDSMetadata(rec.PreferedMetadataImage)).SiteDrive;
                string siteImageDir = Path.Combine(imagesDir, siteDrive);
                PathHelper.EnsureExists(siteImageDir);
                var outfile = Path.Combine(siteImageDir, rec.FilenameBase + ".IMG.jpg");
                if (File.Exists(outfile))
                {
                    return;
                }
                Image.Load(rec.PreferedImage).Save<byte>(outfile);
            });
            var manifest = new LegacySceneManfiest();
            foreach (var group in groupedImageData)
            {
                var sd = new LegacySceneManfiest.SiteDriveData()
                {
                    SiteDrive = new SiteDrive(group.Key),
                    Transform = Matrix.Identity,
                    Images = group.ToList(),
                    Primary = group.Key == primarySiteDrive
                };                
                manifest.AddSiteDrive(sd);
            }
            string content = manifest.Create();
            string sceneSiteDriveFolder = Path.Combine(sceneDir, Path.Combine("ds" + primarySiteDrive, "201801010000"));
            PathHelper.EnsureExists(sceneSiteDriveFolder);
            manifestPath = Path.Combine(sceneSiteDriveFolder, "manifest.xml");
            File.WriteAllText(manifestPath, content);

            string tileDir = Path.Combine(sceneSiteDriveFolder, "tile3d_2.0");
            PathHelper.EnsureExists(tileDir);
            File.WriteAllText(Path.Combine(tileDir, "tilesetSky.json"), LegacySceneManfiest.SkyTilesetContent);
        }

        public int Run()
        {
            Task tilingTask = null;
            if (options.RunTilingServer)
            {
                tilingTask = new Task(() =>
                {
                    var opts = new StartWorkerOptions()
                    {
                        StartMaster = true,
                        SingleThreaded = false
                    };
                    var worker = new StartWorker(opts);
                    worker.Run();
                });
                tilingTask.Start();
            }

            if (options.WorkingDir == null)
            {
                options.WorkingDir = TemporaryFile.TemporaryDirectory;
            }

            var fileRecords = IndexFiles(options.SearchLocations);
            logger.Info("Total files found: " + fileRecords.Count());           

            var imageRecords = fileRecords.Where(rec => rec.HasImage && rec.HasMetadata && (rec.RAS || rec.RASL) && !rec.Thumbnail);
            logger.Info("Total Image files found: " + imageRecords.Count());
            HashSet<string> raslRecords = new HashSet<string>(imageRecords.Where(rec => rec.RASL).Select(rec => rec.FilenameBase));
            imageRecords = imageRecords.Where(rec => rec.RASL || (rec.RAS && !raslRecords.Contains(rec.RASLBaseName)));
            logger.Info("Images after linear filter: " + imageRecords.Count());
            if (options.ImageInclude != null)
            {
                var productIds = ReadFilterFile(options.ImageInclude);
                imageRecords = imageRecords.Where(rec => productIds.Contains(rec.FilenameBase));
                logger.Info("Filtered images down to: " + imageRecords.Count());
            }
            if (options.ImageExclude != null)
            {
                var productIds = ReadFilterFile(options.ImageExclude);
                imageRecords = imageRecords.Where(rec => !productIds.Contains(rec.FilenameBase));
                logger.Info("Filtered images down to: " + imageRecords.Count());
            }
            var meshRecords = fileRecords.Where(rec => rec.RASL && rec.HasMesh && rec.IsLeft && rec.HasImage && rec.HasMetadata && (rec.Nav || (options.MeshMastcam && rec.Mast) || (options.MeshHazcam && rec.Haz)));
            if (options.MeshInclude != null)
            {
                var productIds = ReadFilterFile(options.MeshInclude);
                meshRecords = meshRecords.Where(rec => productIds.Contains(rec.FilenameBase));
                logger.Info("Filtered meshes down to: " + meshRecords.Count());
            }
            if (options.MeshExclude != null)
            {
                var productIds = ReadFilterFile(options.MeshExclude);
                meshRecords = meshRecords.Where(rec => !productIds.Contains(rec.FilenameBase));
                logger.Info("Filtered meshes down to: " + meshRecords.Count());
            }
            logger.Info("Total Mesh files found: " + meshRecords.Count());

            logger.Info("Downloading Files");
            var downloadDirectory = Path.Combine(options.WorkingDir, "Download");
            PathHelper.EnsureExists(downloadDirectory);

            var downloadedRASLRecords = DownloadAndConvertToLocal(imageRecords, downloadDirectory, imagesOnly: true);
            var downloadedMeshRecords = DownloadAndConvertToLocal(meshRecords, downloadDirectory);

            foreach(var mr in downloadedMeshRecords)
            {
                var parser = new PDSParser(new PDSMetadata(mr.PreferedMetadataImage));
                var sd = parser.SiteDrive;
                string folder = Path.Combine(downloadDirectory, sd.ToString());
                PathHelper.EnsureExists(folder);
                File.Copy(mr.PreferedMesh, PathHelper.ChangeDirectory(mr.PreferedMesh, folder));
                File.Copy(mr.PreferedImage, PathHelper.ChangeDirectory(mr.PreferedImage, folder));
                File.Copy(mr.MTL, PathHelper.ChangeDirectory(mr.MTL, folder));
            }


            logger.Info("Creating legacy scene");
            CreateLegacyScene(downloadedRASLRecords, options.WorkingDir, out string manifestPath);

            logger.Info("Processing Meshes");
            var processedDirectory = Path.Combine(options.WorkingDir, "Processed");
            PathHelper.EnsureExists(processedDirectory);
            var processedRecords = ProcessMeshes(downloadedMeshRecords, processedDirectory, options.DecimationRatio);
            string projectName = options.ProjectName;// + "_" + rec.FilenameBase;
            logger.Info("Creating tiling Project: " + projectName);


            int r;
            var createOptions = new CreateProjectOptions()
            {
                ProjectName = projectName,
                TilingScheme = TilingScheme.QuadY,
                SkirtMode = Geometry.SkirtMode.None,
                ReconMethod = Geometry.MeshReconMethod.FSSR,
                FacesPerTile = 2000,
                TileResolution = 256,
                ProjectType = PipelineStateMachine.ProjectType.GenericTiling,
                NoWait = false,
                MaxLeafGroupSize = 32
            };
            
            var createProject = new CreateProject(createOptions);
            r = createProject.Run();

            foreach (var rec in processedRecords)
            {
                var uploadOptions = new UploadInputOptions()
                {
                    ProjectName = projectName,
                    MeshFilepath = rec.PreferedMesh,
                    ImageFilepath = rec.PreferedImage,
                    TileId = null,
                    NoWait = false
                };
                r = new UploadInput(uploadOptions).Run();               
            }
            var runOptions = new RunProjectOptions()
            {
                ProjectName = projectName
            };
            r = new RunProject(runOptions).Run();
            
            var tilesetUrl = createProject.GetStorageUrl("www", projectName, "tileset.json");
            logger.Info("Building tileset.  When done copy data from " + tilesetUrl + " to tile3d_2.0 directory");
            if (tilingTask != null)
            {
                tilingTask.Wait();
            }
            return 0;
        }

        /// <summary>
        /// Convert a mesh 
        /// From: Right-handed Z down
        /// To: Right-handed Y up with a 90 degree rotation
        /// This is more unity like but is still right handed
        /// </summary>
        /// <param name="mesh"></param>
        public static void ConvertVectorToYUp(ref Vector3 v)
        {
            v = new Vector3(-v.Y, -v.Z, v.X);
        }

        /// <summary>
        /// Convert a mesh 
        /// From: Right-handed Z down
        /// To: Right-handed Y up with a 90 degree rotation
        /// This is more unity like but is still right handed
        /// </summary>
        /// <param name="mesh"></param>
        public static void ConvertMeshToYUp(Mesh mesh)
        {
            foreach (var v in mesh.Vertices)
            {
                ConvertVectorToYUp(ref v.Position);
            }

            if(mesh.HasNormals)
            {
                foreach (var v in mesh.Vertices)
                {
                    ConvertVectorToYUp(ref v.Normal);
                }
            }
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