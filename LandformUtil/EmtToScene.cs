using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Xml;
using Microsoft.Xna.Framework;
using CommandLine;
using log4net;
using OPS.Util;
using OPS.Imaging;
using OPS.Geometry;
using OPS.Cloud;
using OPS.Pipeline;
using OPS.Pipeline.TilingServer;
using OPS.Landform;
using OPS.TilingServer;

namespace OPS.LandformUtil
{
    [Verb("emttoscene", HelpText = "Convert emt data into an ASTTRO scene")]
    public class EmtToSceneOptions : LandformCommandOptions
    {
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

    public class EmtToScene /* TODO : LandformCommand */
    {
        EmtToSceneOptions options;

        private static readonly ILog emtLogger = LogManager.GetLogger(typeof(EmtToScene));

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
                emtLogger.Info("Searching " + searchDir);
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
                emtLogger.Info("Downloaded: " + Path.GetFileName(localPath));
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
                emtLogger.Info("Processing: " + Path.GetFileName(localRecord.PreferedMesh));
                Mesh m = Mesh.Load(localRecord.PreferedMesh);
                if(m.Faces.Count == 0)
                {
                    empty.Add(processedRecord);
                    return;
                }
                var parser = new PDSParser(new PDSMetadata(localRecord.PreferedMetadataImage));

                if (!m.HasNormals || options.ForceNormalComputation)
                {
                    emtLogger.Info("Input mesh missing normals or force normal computation is set, generating normals");
                    m.GenerateVertexNormals();
                }
                if (decimationRatio.HasValue)
                {
                    int targetFaces = (int)(m.Faces.Count * decimationRatio.Value);
                    emtLogger.Info("Decimating: " + m.Faces.Count + " down to " + targetFaces);
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
        
        static public string GetTilesetDir(string workingDir, string primarySiteDrive, string sceneName = "Scene")
        {
            string sceneDir = Path.Combine(workingDir, sceneName);
            string sceneSiteDriveFolder = Path.Combine(sceneDir, Path.Combine("ds" + primarySiteDrive, "201801010000"));
            string tileDir = Path.Combine(sceneSiteDriveFolder, "tile3d_2.0");
            return StringHelper.NormalizeSlashes(tileDir,true);
        }

        static public void CreateLegacyScene(IEnumerable<FileRecord> localFileRecords, string workingDir, out string manifestPath, ILog logger, string primarySiteDrive = null, string sceneName = "Scene")
        {
            string sceneDir = Path.Combine(workingDir, sceneName);
            string imagesDir = Path.Combine(workingDir, "images");
            imagesDir = StringHelper.NormalizeSlashes(imagesDir);
            PathHelper.EnsureExists(imagesDir);

            ConcurrentBag<LegacySceneManifest.ImageData> imageDatas = new ConcurrentBag<LegacySceneManifest.ImageData>();
            CoreLimitedParallel.ForEach(localFileRecords, rec =>
            {
                if (!rec.HasMetadata)
                    return;

                var imageData = new LegacySceneManifest.ImageData()
                {
                    FileId = rec.FilenameBase,
                    Metadata = new PDSMetadata(rec.PreferedMetadataImage)
                };
                imageDatas.Add(imageData);
            });
            imageDatas = new ConcurrentBag<LegacySceneManifest.ImageData>(imageDatas.Where(id => new PDSParser(id.Metadata).SiteDrive != null));
            var groupedImageData = imageDatas.GroupBy(id => new PDSParser(id.Metadata).SiteDrive.ToString());

            if (primarySiteDrive == null)
            {
                primarySiteDrive = groupedImageData.Select(g => g.Key).OrderBy(x => x).Last();
            }
            logger.Info("Converting images for scene");
            Serial.ForEach(localFileRecords, rec => 
            {
                if (!rec.HasMetadata || !rec.HasImage)
                    return;

                string siteDrive = new PDSParser(new PDSMetadata(rec.PreferedMetadataImage)).SiteDrive;
                string siteImageDir = Path.Combine(imagesDir, siteDrive);
                siteImageDir = StringHelper.NormalizeSlashes(siteImageDir);
                PathHelper.EnsureExists(siteImageDir);
                
                var outfile = Path.Combine(siteImageDir, rec.FilenameBase + ".IMG.jpg");
                outfile = StringHelper.NormalizeSlashes(outfile);
                if (File.Exists(outfile))
                {
                    return;
                }
                Image.Load(rec.PreferedImage).Save<byte>(outfile);
            });
            var manifest = new LegacySceneManifest(logger);
            foreach (var group in groupedImageData)
            {
                var sd = new LegacySceneManifest.SiteDriveData()
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
            sceneSiteDriveFolder = StringHelper.NormalizeSlashes(sceneSiteDriveFolder);
            PathHelper.EnsureExists(sceneSiteDriveFolder);

            manifestPath = Path.Combine(sceneSiteDriveFolder, "manifest.xml");
            manifestPath = StringHelper.NormalizeSlashes(manifestPath);
            File.WriteAllText(manifestPath, content);

            string tileDir = Path.Combine(sceneSiteDriveFolder, "tile3d_2.0");
            sceneSiteDriveFolder = StringHelper.NormalizeSlashes(sceneSiteDriveFolder);
            PathHelper.EnsureExists(tileDir);
            File.WriteAllText(Path.Combine(tileDir, "tilesetSky.json"), LegacySceneManifest.SkyTilesetContent);
        }

        public int Run()
        {
            Task tilingTask = null;
            if (options.RunTilingServer && options.Cloud)
            {
                tilingTask = new Task(() =>
                {
                    var opts = new StartMasterOptions() { StartWorker = true, SingleThreaded = false };
                    var master = new StartMaster(opts);
                    master.Run();
                });
                tilingTask.Start();
            }

            if (options.WorkingDir == null)
            {
                options.WorkingDir = TemporaryFile.TemporaryDirectory;
            }

            var fileRecords = IndexFiles(options.SearchLocations);
            emtLogger.Info("Total files found: " + fileRecords.Count());           

            var imageRecords = fileRecords.Where(rec => rec.HasImage && rec.HasMetadata && (rec.RAS || rec.RASL) && !rec.Thumbnail);
            emtLogger.Info("Total Image files found: " + imageRecords.Count());
            HashSet<string> raslRecords = new HashSet<string>(imageRecords.Where(rec => rec.RASL).Select(rec => rec.FilenameBase));
            imageRecords = imageRecords.Where(rec => rec.RASL || (rec.RAS && !raslRecords.Contains(rec.RASLBaseName)));
            emtLogger.Info("Images after linear filter: " + imageRecords.Count());
            if (options.ImageInclude != null)
            {
                var productIds = ReadFilterFile(options.ImageInclude);
                imageRecords = imageRecords.Where(rec => productIds.Contains(rec.FilenameBase));
                emtLogger.Info("Filtered images down to: " + imageRecords.Count());
            }
            if (options.ImageExclude != null)
            {
                var productIds = ReadFilterFile(options.ImageExclude);
                imageRecords = imageRecords.Where(rec => !productIds.Contains(rec.FilenameBase));
                emtLogger.Info("Filtered images down to: " + imageRecords.Count());
            }
            var meshRecords = fileRecords.Where(rec => rec.RASL && rec.HasMesh && rec.IsLeft && rec.HasImage && rec.HasMetadata && (rec.Nav || (options.MeshMastcam && rec.Mast) || (options.MeshHazcam && rec.Haz)));
            if (options.MeshInclude != null)
            {
                var productIds = ReadFilterFile(options.MeshInclude);
                meshRecords = meshRecords.Where(rec => productIds.Contains(rec.FilenameBase));
                emtLogger.Info("Filtered meshes down to: " + meshRecords.Count());
            }
            if (options.MeshExclude != null)
            {
                var productIds = ReadFilterFile(options.MeshExclude);
                meshRecords = meshRecords.Where(rec => !productIds.Contains(rec.FilenameBase));
                emtLogger.Info("Filtered meshes down to: " + meshRecords.Count());
            }
            emtLogger.Info("Total Mesh files found: " + meshRecords.Count());

            emtLogger.Info("Downloading Files");
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


            emtLogger.Info("Creating legacy scene");
            CreateLegacyScene(downloadedRASLRecords, options.WorkingDir, out string manifestPath, emtLogger);

            emtLogger.Info("Processing Meshes");
            var processedDirectory = Path.Combine(options.WorkingDir, "Processed");
            PathHelper.EnsureExists(processedDirectory);
            var processedRecords = ProcessMeshes(downloadedMeshRecords, processedDirectory, options.DecimationRatio);
            string projectName = options.ProjectName;// + "_" + rec.FilenameBase;
            emtLogger.Info("Creating tiling Project: " + projectName);

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
                MaxLeafGroupSize = 32,
                Local = !options.Cloud
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
                    NoWait = false,
                    Local = !options.Cloud
                };
                r = new UploadInput(uploadOptions).Run();               
            }
            var runOptions = new RunProjectOptions()
            {
                ProjectName = projectName,
                Local = !options.Cloud
            };
            r = new RunProject(runOptions).Run();
            
            var tilesetUrl = createProject.GetPipeline().GetStorageUrl("www", projectName, "tileset.json");
            emtLogger.Info("Building tileset.  When done copy data from " + tilesetUrl + " to tile3d_2.0 directory");

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

    public class LegacySceneManifest
    {
        private ILog logger;

        public class ImageData
        {
            public string FileId;
            public PDSMetadata Metadata;
        } 

        public class SiteDriveData
        {
            public SiteDrive SiteDrive;
            public int StartSol;
            public int EndSol ;
            public bool Primary;
            public Matrix Transform;
            public List<ImageData> Images = new List<ImageData>();
        }

        List<SiteDriveData> data = new List<SiteDriveData>();

        public LegacySceneManifest(ILog logger)
        {
            this.logger = logger;
        }

        public void AddSiteDrive(SiteDriveData d)
        {
            this.data.Add(d);
        }


        public string Create()
        {
            XmlDocument doc = new XmlDocument();
            XmlDeclaration xmldecl;
            xmldecl = doc.CreateXmlDeclaration("1.0", null, null);
            xmldecl.Encoding = "UTF-8";
            xmldecl.Standalone = "yes";
            XmlElement root = doc.DocumentElement;
            doc.InsertBefore(xmldecl, root);

            XmlElement sceneEl = (XmlElement)doc.AppendChild(doc.CreateElement("scene"));
            WriteMetaData(doc, sceneEl);
            WriteProjections(doc, sceneEl);


            return Beautify(doc);

        }

        public string Beautify(XmlDocument doc)
        {
            StringBuilder sb = new StringBuilder();
            XmlWriterSettings settings = new XmlWriterSettings();
            settings.Indent = true;
            settings.IndentChars = "  ";
            settings.NewLineChars = "\r\n";
            settings.NewLineHandling = NewLineHandling.Replace;
            settings.OmitXmlDeclaration = true;

            using (XmlWriter writer = XmlWriter.Create(sb, settings))
            {
                doc.Save(writer);
            }
            return sb.ToString();
        }

        SiteDriveData Primary
        {
            get { return data.First(x => x.Primary);  }
        }

        void WriteMetaData(XmlDocument doc, XmlElement parent)
        {
            AddSingleValueTag(doc, parent, "id", "ds" + Primary.SiteDrive.ToString());
            AddSingleValueTag(doc, parent, "title", "TITLE");
            AddSingleValueTag(doc, parent, "version", "201801010000");
            AddSingleValueTag(doc, parent, "pipelineVersion", "0.1.16");
            AddSingleValueTag(doc, parent, "extent", "4096");
            AddSingleValueTag(doc, parent, "geometryPath", ".");
            AddSingleValueTag(doc, parent, "geometrySource", ".");
            AddSingleValueTag(doc, parent, "imagePath", ".");
            AddSingleValueTag(doc, parent, "buildDuration", "0");
            WriteCoverage(doc, parent);
            WriteSitedriveData(doc, parent);
        }

        void WriteSitedriveData(XmlDocument doc, XmlElement parent)
        {
            XmlElement allSitedrives = doc.CreateElement("sitedrives");
            parent.AppendChild(allSitedrives);

            foreach (var sitedrive in data)
            {
                // Note: calling GetSolRange on each iteration is a little inefficient as this method does
                // a linear pass through the source images. But in practice it is fast enough.

                XmlElement thisSitedrive = doc.CreateElement("sitedrive");
                thisSitedrive.SetAttribute("id", sitedrive.SiteDrive.ToString());
                if (sitedrive.Primary)
                {
                    thisSitedrive.SetAttribute("primary", "true");
                }
                thisSitedrive.SetAttribute("startSol", sitedrive.StartSol.ToString());
                thisSitedrive.SetAttribute("endSol", sitedrive.EndSol.ToString());
                allSitedrives.AppendChild(thisSitedrive);
            }
        }

        void WriteCoverage(XmlDocument doc, XmlElement parent)
        {
            XmlElement coverageEl = doc.CreateElement("coverage");
            parent.AppendChild(coverageEl);
            coverageEl.SetAttribute("color", string.Format("{0:0.0000}", 0.25));
            coverageEl.SetAttribute("grayscale", string.Format("{0:0.0000}", 0.5));
            coverageEl.SetAttribute("orbital", string.Format("{0:0.0000}", 0.25));
        }

        void AddSingleValueTag(XmlDocument doc, XmlElement parent, string name, string value)
        {
            XmlElement e = doc.CreateElement(name);
            e.InnerText = value;
            parent.AppendChild(e);
        }


        void WriteProjections(XmlDocument doc, XmlElement parent)
        {
            XmlElement projectionsEl = doc.CreateElement("projections");

            XmlElement transformsEl = doc.CreateElement("transforms");
            foreach (var sitedrive in data)
            {
                XmlElement transformEl = doc.CreateElement("transform");

                XmlElement siteDriveEl = doc.CreateElement("sitedrive");
                siteDriveEl.InnerText = sitedrive.SiteDrive.ToString();
                transformEl.AppendChild(siteDriveEl);

                var matrixString = string.Join(" ", Tile3DBuilder.MatrixToList(sitedrive.Transform).Select(x => x.ToString()));
                XmlElement matrixEl = doc.CreateElement("primary_to_local_level_matrix");
                matrixEl.InnerText = matrixString;
                transformEl.AppendChild(matrixEl);

                transformsEl.AppendChild(transformEl);
            }
            projectionsEl.AppendChild(transformsEl);

            XmlElement imagesEl = doc.CreateElement("images");
            foreach (var sitedrive in data)
            {
                foreach (var imageData in sitedrive.Images)
                {
                    WriteImageMetadata(doc, imagesEl, sitedrive, imageData);
                }
            }

            projectionsEl.AppendChild(imagesEl);
            parent.AppendChild(projectionsEl);
        }

        void WriteImageMetadata(XmlDocument doc, XmlElement parentElement, SiteDriveData siteData, ImageData imageData)
        {

            XmlElement imageEl = doc.CreateElement("image");
            imageEl.SetAttribute("id", imageData.FileId);

            PDSParser p = new PDSParser(imageData.Metadata);

            XmlElement dimEl = doc.CreateElement("dimensions");
            XmlElement originEl = doc.CreateElement("origin");
            // Assume 1,1 as a fall back in cases where this isn't defined
            var firstLine = 1;
            var firstSample = 1;
            try
            {
                firstLine = p.FirstLine;
                firstSample = p.FirstSample;
            }
            catch
            {
                logger.Warn("Missing first line and sample in metadata, assuming 1,1");
            }

            originEl.InnerText = firstLine + "," + firstSample;
            XmlElement widthEl = doc.CreateElement("width");
            XmlElement heightEl = doc.CreateElement("height");
            XmlElement firstLineEl = doc.CreateElement("first_line");
            XmlElement firstLineSampleEl = doc.CreateElement("first_line_sample");

            AddSingleValueTag(doc, imageEl, "product", "RAS");
            AddSingleValueTag(doc, imageEl, "camera", imageData.FileId.Substring(0,2));

            XmlElement fov = doc.CreateElement("fov_radians");
            XmlElement sitedrive = doc.CreateElement("sitedrive");
            widthEl.InnerText = imageData.Metadata.Width.ToString();
            heightEl.InnerText = imageData.Metadata.Height.ToString();
            firstLineEl.InnerText = firstLine.ToString();
            firstLineSampleEl.InnerText = firstSample.ToString();
            dimEl.AppendChild(widthEl);
            dimEl.AppendChild(heightEl);
            dimEl.AppendChild(firstLineEl);
            dimEl.AppendChild(firstLineSampleEl);
            dimEl.AppendChild(originEl);

            Quaternion qvals = p.RoverOriginRotation;
            XmlElement rotationQuaternion = doc.CreateElement("rover_rotation");
            rotationQuaternion.InnerText = string.Format("{0} {1} {2} {3}", qvals.W, qvals.X, qvals.Y, qvals.Z);
            imageEl.AppendChild(rotationQuaternion);

            // Compute from camera model in cases where this isn't defined
            double hfov = 0;
            try
            {
                hfov =  p.HorizontalFOV;
            }
            catch
            {
                logger.Warn("Missing hfov in metadata, estimating based on camera model");
                var r1 = imageData.Metadata.CameraModel.Unproject(new Vector2(0, imageData.Metadata.Height / 2));
                var r2 = imageData.Metadata.CameraModel.Unproject(new Vector2(imageData.Metadata.Width, imageData.Metadata.Height / 2));
                hfov = EdgeCollapse.Angle(r1.Direction, r2.Direction); // 0.411114050636516 was hardcoded for M2020 ROASTT;
            }
            fov.InnerText = string.Format("{0}", hfov);
            sitedrive.InnerText = siteData.SiteDrive.ToString();
            
            imageEl.AppendChild(dimEl);
            imageEl.AppendChild(fov);
            imageEl.AppendChild(sitedrive);

            var cm = imageData.Metadata.CameraModel;
            if (cm.GetType() == typeof(CAHV))
            {
                XmlElement camModelEl = CAHVToXml(doc, (CAHV)cm);
                imageEl.AppendChild(camModelEl);
            }
            else if(cm.GetType() == typeof(CAHVOR))
            {
                XmlElement camModelEl = CAHVToXml(doc, (CAHVOR)cm);
                imageEl.AppendChild(camModelEl);
            }
            else if (cm.GetType() == typeof(CAHVORE))
            {
                XmlElement camModelEl = CAHVOREToXml(doc, (CAHVORE)cm);
                imageEl.AppendChild(camModelEl);
            }
            else
            {
                throw new NotImplementedException("Not enough cameramodels - cannot do cavhore");
            }

            parentElement.AppendChild(imageEl);
        }

        public XmlElement CAHVToXml(XmlDocument doc, CAHV model)
        {
            XmlElement camModel = doc.CreateElement("camera_model");
            camModel.SetAttribute("type", "CAHV");

            XmlElement cEl = CreateVectorElement(doc, model.C, "c");
            XmlElement aEl = CreateVectorElement(doc, model.A, "a");
            XmlElement hEl = CreateVectorElement(doc, model.H, "h");
            XmlElement vEl = CreateVectorElement(doc, model.V, "v");

            camModel.AppendChild(cEl);
            camModel.AppendChild(aEl);
            camModel.AppendChild(hEl);
            camModel.AppendChild(vEl);
            return camModel;
        }

        public XmlElement CAHVORToXml(XmlDocument doc, CAHVOR model)
        {
            XmlElement camModel = doc.CreateElement("camera_model");
            camModel.SetAttribute("type", "CAHVOR");

            XmlElement cEl = CreateVectorElement(doc, model.C, "c");
            XmlElement aEl = CreateVectorElement(doc, model.A, "a");
            XmlElement hEl = CreateVectorElement(doc, model.H, "h");
            XmlElement vEl = CreateVectorElement(doc, model.V, "v");
            XmlElement oEl = CreateVectorElement(doc, model.O, "o");
            XmlElement rEl = CreateVectorElement(doc, model.R, "r");

            camModel.AppendChild(cEl);
            camModel.AppendChild(aEl);
            camModel.AppendChild(hEl);
            camModel.AppendChild(vEl);
            camModel.AppendChild(oEl);
            camModel.AppendChild(rEl);

            return camModel;
        }

        public XmlElement CAHVOREToXml(XmlDocument doc, CAHVORE model)
        {
            XmlElement camModel = doc.CreateElement("camera_model");
            camModel.SetAttribute("type", "CAHVORE");

            XmlElement cEl = CreateVectorElement(doc, model.C, "c");
            XmlElement aEl = CreateVectorElement(doc, model.A, "a");
            XmlElement hEl = CreateVectorElement(doc, model.H, "h");
            XmlElement vEl = CreateVectorElement(doc, model.V, "v");
            XmlElement oEl = CreateVectorElement(doc, model.O, "o");
            XmlElement rEl = CreateVectorElement(doc, model.R, "r");
            XmlElement eEl = CreateVectorElement(doc, model.E, "e");

            camModel.AppendChild(cEl);
            camModel.AppendChild(aEl);
            camModel.AppendChild(hEl);
            camModel.AppendChild(vEl);
            camModel.AppendChild(oEl);
            camModel.AppendChild(rEl);
            camModel.AppendChild(eEl);

            return camModel;
        }

        protected XmlElement CreateVectorElement(XmlDocument doc, Vector3 vec, string elementName)
        {
            XmlElement el = doc.CreateElement(elementName);
            el.InnerText = string.Join(",", vec.X, vec.Y, vec.Z);
            return el;
        }
        
        public const string SkyTilesetContent = @"{
  ""asset"": {
    ""version"": ""1.0"",
    ""gltfUpAxis"": ""Z""
  },
  ""root"": {
    ""boundingVolume"": {
      ""box"": [
        0,
        768.3815307617188,
        0,
        8192,
        0,
        0,
        0,
        2366.5615844726562,
        0,
        0,
        0,
        8192
      ]
    },
    ""transform"": [
      1,
      0,
      0,
      0,
      0,
      1,
      0,
      0,
      0,
      0,
      1,
      0,
      0,
      0,
      0,
      1
    ],
    ""children"": [],
    ""geometricError"": 16384,
    ""refine"": ""REPLACE""
  },
  ""geometricError"": 16384
}";

    }
}
