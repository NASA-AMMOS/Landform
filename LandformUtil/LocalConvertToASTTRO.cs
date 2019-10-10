using CommandLine;
using Microsoft.Xna.Framework;
using OPS.Imaging;
using OPS.Landform;
using OPS.Pipeline;
using OPS.Pipeline.AlignmentServer;
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
        // input related
        [Option(Default = null, HelpText = "input tileset json (tiles assumed to be in same folder), search project storage if omitted")]
        public string InputTileset { get; set; }
      
        [Option(Required = false, Default = "ply", HelpText = "input mesh Extension")]
        public string InputMeshExtension { get; set; }

        [Option(Required = false, Default = "jpg", HelpText = "input mesh Extension")]
        public string InputImageExtension { get; set; }

        [Option(Required = false, Default = "b3dm", HelpText = "output mesh Extension")]
        public string OutputMeshExtension { get; set; }
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
            }
            catch (Exception ex)
            {
                pipeline.LogException(ex);
                return 1;
            }

            StopStopwatch();

            return 0;
        }
            /*
          
           
          

            pipeline.LogInfo("rotating meshes to ASTTRO frame");
            string tilesetDir = EmtToScene.GetTilesetDir(outputPath, options.MeshFrame);

            CoreLimitedParallel.ForEach(Directory.EnumerateFiles(inputDir, "*." + options.InputMeshExtension), inputFilePath =>
            {
                // read every mesh in and convert to asttro coordinate frame
                pipeline.LogInfo("Transforming data: {0}", inputFilePath);
                Mesh mesh = Mesh.Load(inputFilePath);
                EmtToScene.ConvertMeshToYUp(mesh);
                string outputFilePath = Path.Combine(tilesetDir, Path.ChangeExtension(Path.GetFileName(inputFilePath),options.OutputMeshExtension));
                string inputImagePath = Path.Combine(inputDir, Path.ChangeExtension(Path.GetFileName(inputFilePath), options.InputImageExtension));
                mesh.Save(outputFilePath, File.Exists(inputImagePath) ? inputImagePath : null);
            }
            );

            pipeline.LogInfo("modifying tileset json");
            {
                string inputTilesetJSONPath = Path.Combine(inputDir, "tileset.json");
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

                string outputJSONPath = Path.Combine(tilesetDir, "tileset.json");
                File.WriteAllText(outputJSONPath, jsonData);
            }

            // create master manifest
            pipeline.LogInfo("Creating master manifest");
            CreateMasterManifest(meshFrame, outputPath, manifestPath, imageObservations);

            pipeline.LogInfo("converted to legacy scene successfully");
            */

            // TODO: upload to s3


        private string BuildASTTROScene()
        {
            //TODO: verify meshframe is newest sitedrive
           
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

        //private void CreateMasterManifest(string outputFrame, string astroOutputPath, string manifestPath,
        //                                 IEnumerable<Observation> imageObservations)
        //{
        //    pipeline.LogInfo("Building master manifest");
        //    int numNavcams = 0;
        //    int numMastcams = 0;
        //    foreach (var obs in imageObservations)
        //    {
        //        PDSParser parser = new PDSParser(new PDSMetadata(new System.Uri(obs.Url).LocalPath));
        //        if (mission.IsNavcam(mission.GetRoverProductCamera(parser.InstrumentId)))
        //            numNavcams++;
        //        else if (mission.IsMastcam(mission.GetRoverProductCamera(parser.InstrumentId)))
        //            numMastcams++;
        //    }
        //    CreateMasterManifest(LocalPathToS3Url(astroOutputPath, manifestPath),
        //                         Path.Combine(astroOutputPath, "mastermanifest.xml"),
        //                         outputFrame, numNavcams, numMastcams);
        //}

        //string LocalPathToS3Url(string localRoot, string localPath)
        //{
        //    string relativePath =
        //        StringHelper.NormalizeSlashes(localPath.Substring(Path.GetDirectoryName(localRoot).Length + 1));
        //    return StringHelper.NormalizeUrl(options.OutputS3Bucket + relativePath, "s3://", false);
        //}

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
