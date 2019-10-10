using CommandLine;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using OPS.Geometry;
using OPS.Imaging;
using OPS.Landform;
using OPS.Pipeline;
using OPS.Pipeline.Tile3D;
using OPS.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;

namespace OPS.LandformUtil
{
    [Verb("local-convert-to-ASTTRO", HelpText = "convert a tileset to a ASTTRO scene")]
    public class LocalConvertToASTTROOptions : TextureCommandOptions
    {
        // input related //TODO: implement
        //[Option(Default = null, HelpText = "input tileset json (tiles assumed to be in same folder), search project storage if omitted")]
        //public string InputTileset { get; set; }

        [Option(Required = false, Default = "b3dm", HelpText = "output mesh Extension")] //should pull from tiling project?
        public string OutputMeshExtension { get; set; }

        [Option(Required = false, Default = "jpg", HelpText = "output image Extension")]
        public string OutputImageExtension { get; set; }

        [Option(Required = false, Default = "m20-rps-asttro-terrain", HelpText = "output s3 bucket (used for pointing in master manifest)")]
        public string OutputS3Bucket { get; set; }
    }

    public class LocalConvertToASTTRO : TextureCommand
    {
        private const string OutputDirectory = "ASTTRO";

        private LocalConvertToASTTROOptions options;
        private string legacySceneManifestPath;

        public LocalConvertToASTTRO(LocalConvertToASTTROOptions options) : base(options)
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
                if (mission.IsNavcam(mission.GetRoverProductCamera(parser.InstrumentId)))
                    numNavcams++;
                else if (mission.IsMastcam(mission.GetRoverProductCamera(parser.InstrumentId)))
                    numMastcams++;
            }

            string masterManifestPath = Path.Combine(localOutputPath, "mastermanifest.xml");
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

            string outputTilesetJSONPath = Path.Combine(EmtToScene.GetTilesetDir(localOutputPath, meshFrame), "tileset.json");
            outputTilesetJSONPath = StringHelper.NormalizeSlashes(outputTilesetJSONPath);
            File.WriteAllText(outputTilesetJSONPath, jsonData);
        }

        private void RotateSceneMeshes()
        {
            string tilesetDir = EmtToScene.GetTilesetDir(localOutputPath, meshFrame);
            string inputDir = pipeline.GetLocalFolder(options.OutputFolder, DecorateOutDir(TilingCommand.OUT_DIR), project.Name);
            inputDir = StringHelper.NormalizeSlashes(inputDir);
            CoreLimitedParallel.ForEach(Directory.EnumerateFiles(inputDir, "*." + options.MeshFormat), inputFilePath =>
            {
                // read every mesh in and convert to asttro coordinate frame
                pipeline.LogInfo("Transforming data: {0}", inputFilePath);
                Mesh mesh = Mesh.Load(inputFilePath);
                EmtToScene.ConvertMeshToYUp(mesh);

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
                var RASLRecords = imageObservations.Select(x => new EmtToScene.FileRecord(new System.Uri(x.Url).LocalPath));
                EmtToScene.CreateLegacyScene(RASLRecords, localOutputPath, out manifestPath, pipeline.Logger, meshFrame);
            }

            pipeline.LogInfo("ASTTRO scene manifest written at: {0}", manifestPath);
            return manifestPath;
        }

        private List<double> ConvertBoundingBoxToYUp(List<double> box)
        {
            BoundingBox bb = Tile3DBuilder.BoxToBounds(box);
            EmtToScene.ConvertVectorToYUp(ref bb.Min);
            EmtToScene.ConvertVectorToYUp(ref bb.Max);
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
            manifest.InnerText = CloudPipeline.ConvertS3UrlToHttps(sceneManifestUrl);

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
    }
}
