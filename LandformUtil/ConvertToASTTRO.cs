using CommandLine;
using log4net;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using OPS.Geometry;
using OPS.Imaging;
using OPS.Landform;
using OPS.Pipeline;
using OPS.Pipeline.Tile3D;
using OPS.Util;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;

namespace OPS.LandformUtil
{
    [Verb("convert-to-asttro", HelpText = "convert a tileset to an ASTTRO scene")]
    public class ConvertToASTTROOptions : TextureCommandOptions
    {
        [Option(Required = false, Default = "b3dm", HelpText = "output mesh Extension")]
        public string OutputMeshExtension { get; set; }

        [Option(Required = false, Default = "jpg", HelpText = "output image Extension")]
        public string OutputImageExtension { get; set; }

        [Option(Required = false, Default = "m20-rps-asttro-terrain", HelpText = "output s3 bucket (used for pointing in master manifest)")]
        public string OutputS3Bucket { get; set; }

        [Option(Required = false, Default = ".s3-us-gov-west-1.amazonaws.com", HelpText = "domain used for master manifest")]
        public string BucketDomain { get; set; }
    }

    public class ConvertToASTTRO : TextureCommand
    {
        private const string OutputDirectory = "ASTTRO";

        private ConvertToASTTROOptions options;
        private string legacySceneManifestPath;

        public ConvertToASTTRO(ConvertToASTTROOptions options) : base(options)
        {
            this.options = options;
        }

        public int Run()
        {
            StartStopwatch();

            try
            {
                if (!ParseArgumentsAndLoadCaches(OutputDirectory))
                {
                    return 0; //help
                }

                RunPhase("build scene manifest", () => { legacySceneManifestPath = BuildASTTROScene(); });
                RunPhase("rotate tile meshes to ASTTRO frame", () => { RotateSceneMeshes(); });
                RunPhase("modify tileset JSON", () => { ModifyTilesetJSON(); });
                RunPhase("create master manifest", () => { CreateMasterManifest(); });

                pipeline.LogInfo("converted to legacy scene successfully");

            }
            catch (Exception ex)
            {
                pipeline.LogException(ex);
                return 1;
            }

            StopStopwatch();

            return 0;
        }

        private void CreateMasterManifest()
        {
            int numNavcams = 0;
            int numMastcams = 0;
            foreach (var obs in imageObservations)
            {
                PDSParser parser = new PDSParser(new PDSMetadata(new System.Uri(obs.Url).LocalPath));
                if (mission.IsNavcam(mission.GetCamera(parser)))
                {
                    numNavcams++;
                }
                else if (mission.IsMastcam(mission.GetCamera(parser)))
                {
                    numMastcams++;
                }
            }

            string masterManifestPath = Path.Combine(localOutputPath, "master-landform.xml");
            masterManifestPath = StringHelper.NormalizeSlashes(masterManifestPath);

            CreateMasterManifest(LocalPathToS3Url(localOutputPath,legacySceneManifestPath), masterManifestPath, meshFrame, numNavcams, numMastcams);
        }

        private void ModifyTilesetJSON()
        {
            string inputTileDir = pipeline.GetLocalFolder(options.OutputFolder, DecorateOutDir(TilingCommand.OUT_DIR + "Set"), project.Name);
            string inputTilesetJSONPath = Path.Combine(inputTileDir, "tileset.json");
            inputTilesetJSONPath = StringHelper.NormalizeSlashes(inputTilesetJSONPath);

            string jsonData = File.ReadAllText(inputTilesetJSONPath);
            Pipeline.Tile3D.Tileset tileset = JsonConvert.DeserializeObject<Pipeline.Tile3D.Tileset>(jsonData);

            //convert all bounds to y up
            List<Tile> toProcess = new List<Tile>();
            toProcess.Add(tileset.Root);
            while (toProcess.Any())
            {
                Tile curTile = toProcess.First();
                toProcess.RemoveAt(0);

                curTile.BoundingVolume.Box = ConvertBoundingBoxToYUp(curTile.BoundingVolume.Box);
                toProcess.AddRange(curTile.Children);
            }

            jsonData = JsonConvert.SerializeObject(tileset, Newtonsoft.Json.Formatting.None);

            //HACK: Issue #602: to emulate the previous version of tilest.json that asttro uses
            jsonData = jsonData.Replace("uri", "url");

            string outputTilesetJSONPath = Path.Combine(GetTilesetDir(localOutputPath, meshFrame, project.Name), "tileset.json");
            outputTilesetJSONPath = StringHelper.NormalizeSlashes(outputTilesetJSONPath);
            File.WriteAllText(outputTilesetJSONPath, jsonData);
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

            if (mesh.HasNormals)
            {
                foreach (var v in mesh.Vertices)
                {
                    ConvertVectorToYUp(ref v.Normal);
                }
            }
        }

        private void RotateSceneMeshes()
        {
            string tilesetDir = GetTilesetDir(localOutputPath, meshFrame, project.Name);
            string inputDir = pipeline.GetLocalFolder(options.OutputFolder, DecorateOutDir(TilingCommand.OUT_DIR), project.Name);
            inputDir = StringHelper.NormalizeSlashes(inputDir);
            CoreLimitedParallel.ForEach(Directory.EnumerateFiles(inputDir, "*." + options.MeshFormat), inputFilePath =>
            {
                // read every mesh in and convert to asttro coordinate frame
                pipeline.LogInfo("Transforming data: {0}", inputFilePath);
                Mesh mesh = Mesh.Load(inputFilePath);
                ConvertMeshToYUp(mesh);

                //save data
                string outputFilePath = Path.Combine(tilesetDir, Path.ChangeExtension(Path.GetFileName(inputFilePath), options.OutputMeshExtension));
                outputFilePath = StringHelper.NormalizeSlashes(outputFilePath);

                string inputImagePath = Path.Combine(inputDir, Path.ChangeExtension(Path.GetFileName(inputFilePath), options.ImageFormat));
                inputImagePath = StringHelper.NormalizeSlashes(inputImagePath);

                string outputImagePath = Path.ChangeExtension(inputImagePath, options.OutputImageExtension);
                if (inputImagePath != outputImagePath && !File.Exists(outputImagePath))
                {
                    pipeline.LoadImage(inputImagePath).Save<byte>(outputImagePath);
                }
                mesh.Save(outputFilePath, File.Exists(inputImagePath) ? outputImagePath : null);
            }
            );
        }

        private string BuildASTTROScene()
        {
            string manifestPath = null;
            {
                var imageRecords = imageObservations.Select(x => new FileRecord(new System.Uri(x.Url).LocalPath));
                CreateLegacyScene(imageRecords, localOutputPath, out manifestPath, meshFrame, project.Name);
            }

            pipeline.LogInfo("ASTTRO scene manifest written at: {0}", manifestPath);
            return manifestPath;
        }

        private List<double> ConvertBoundingBoxToYUp(List<double> box)
        {
            BoundingBox bb = Tile3DBuilder.BoxToBounds(box);
            ConvertVectorToYUp(ref bb.Min);
            ConvertVectorToYUp(ref bb.Max);
            return Tile3DBuilder.BoundsToBox(bb);
        }

        string LocalPathToS3Url(string localRoot, string localPath)
        {
            string relativePath = localPath.Substring(Path.GetDirectoryName(localRoot).Length + 1);
            string bucketPath = Path.Combine(options.OutputS3Bucket, relativePath);
            bucketPath = StringHelper.NormalizeSlashes(bucketPath);
            return StringHelper.NormalizeUrl(bucketPath, "s3://", false);
        }

        static private void AddAttributeXml(XmlNode node, string name, string value)
        {
            XmlAttribute att = node.OwnerDocument.CreateAttribute(name);
            att.Value = value;
            node.Attributes.Append(att);

        }

        private void CreateMasterManifest(string sceneManifestUrl, string masterManifestPath, string primarySiteDrive,
                                              int navCams, int mastCams)
        {
            XmlDocument doc = new XmlDocument();
            XmlDeclaration xmldecl;
            xmldecl = doc.CreateXmlDeclaration("1.0", null, null);
            xmldecl.Encoding = "UTF-8";
            xmldecl.Standalone = "yes";
            XmlElement root = doc.DocumentElement;
            doc.InsertBefore(xmldecl, root);

            XmlElement scenes = (XmlElement)doc.AppendChild(doc.CreateElement("scenes"));
            XmlElement scene = (XmlElement)scenes.AppendChild(doc.CreateElement("scene"));
            AddAttributeXml(scene, "id", "ds" + primarySiteDrive);  //eg. "ds0000100372"
            AddAttributeXml(scene, "type", "master");

            SiteDrive primarySD = new SiteDrive(primarySiteDrive);
            AddAttributeXml(scene, "primarySite", primarySD.Site.ToString());
            AddAttributeXml(scene, "primaryDrive", primarySD.Drive.ToString());
            XmlElement manifests = (XmlElement)scene.AppendChild(doc.CreateElement("manifests"));
            XmlElement manifest = (XmlElement)manifests.AppendChild(doc.CreateElement("manifest"));
            AddAttributeXml(manifest, "version", "201801010000");
            AddAttributeXml(manifest, "pipelineVersion", "0.1.15");
            AddAttributeXml(manifest, "startSol", "0");
            AddAttributeXml(manifest, "endSol", "0");
            AddAttributeXml(manifest, "navcamCount", navCams.ToString());
            AddAttributeXml(manifest, "hazcamCount", mastCams.ToString());
            AddAttributeXml(manifest, "mastcamCount", "0");
            AddAttributeXml(manifest, "color", "0.0");
            AddAttributeXml(manifest, "grayscale", "1.0");
            AddAttributeXml(manifest, "orbital", "0.0");
            manifest.InnerText = CloudPipeline.ConvertS3UrlToHttps(sceneManifestUrl,options.BucketDomain);

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

            File.WriteAllText(masterManifestPath, sb.ToString());
        }

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
                if (this.IV != null)
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
                    if (this.OBJ != null)
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
                if (GetFilenameBase(filename) != this.FilenameBase)
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

        protected string GetTilesetDir(string workingDir, string primarySiteDrive, string sceneName = "Scene")
        {
            string sceneDir = Path.Combine(workingDir, sceneName);
            string sceneSiteDriveFolder = Path.Combine(sceneDir, Path.Combine("ds" + primarySiteDrive, "201801010000"));
            string tileDir = Path.Combine(sceneSiteDriveFolder, "tile3d_2.0");
            return StringHelper.NormalizeSlashes(tileDir, true);
        }

        protected void CreateLegacyScene(IEnumerable<FileRecord> localFileRecords, string workingDir, out string manifestPath, string primarySiteDrive = null, string sceneName = "Scene")
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
            pipeline.LogInfo("Converting images for scene");
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
            var manifest = new LegacySceneManifest(pipeline.Logger);

            Pipeline.AlignmentServer.Frame primaryFrame = frameCache.GetFrame(primarySiteDrive);
            foreach (var group in groupedImageData)
            {
                var sd = new LegacySceneManifest.SiteDriveData()
                {
                    SiteDrive = new SiteDrive(group.Key),
                    Transform = frameCache.GetRelativeTransform(frameCache.GetFrame(group.Key), primaryFrame, options.UsePriors, options.OnlyAligned).Mean,
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
                public int EndSol;
                public bool Primary;
                public Matrix Transform;
                public List<ImageData> Images = new List<ImageData>();
            }

            List<SiteDriveData> data = new List<SiteDriveData>();

            public LegacySceneManifest(log4net.ILog logger)
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
                get { return data.First(x => x.Primary); }
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
                AddSingleValueTag(doc, imageEl, "camera", imageData.FileId.Substring(0, 2));

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
                    hfov = p.HorizontalFOV;
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
                else if (cm.GetType() == typeof(CAHVOR))
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
}
