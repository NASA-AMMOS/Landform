using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Diagnostics;
using CommandLine;
using Microsoft.Xna.Framework;
using OPS.Util;
using OPS.Imaging;
using OPS.RayTrace;
using OPS.Geometry;
using OPS.Pipeline;
using OPS.Pipeline.AlignmentServer;
using OPS.Pipeline.TilingServer;
using System.Threading;
using Newtonsoft.Json;
using System.Xml;
using System.Text;

namespace OPS.Landform
{
    [Verb("local-convert-to-legacy-scene", HelpText = "convert a tileset to a legacy ASTTRO scene")]
    public class LocalConvertToLegacySceneOptions : LandformCommandOptions
    {
        // input related
        [Option(Default = null, HelpText = "input tileset json (tiles assumed to be in same folder), search project storage if omitted")]
        public string InputTileset { get; set; }

        [Option(HelpText = "Mesh coordinate frame: a numeric sitedrive SSSSSDDDDD and primary sitedrive in scene", Default = null)]
        public string MeshFrame { get; set; }

        [Option(Required = false, Default = "b3dm", HelpText = "Mesh Extension")]
        public string MeshExtension { get; set; }

        // output related
        [Option(HelpText = "output local directory, or omit to save to project storage", Default = null)]
        public string OutputFolder { get; set; }

        [Option(Required = false, HelpText = "a url to a bucket in the form: s3://<bucket>/<path>/ ")]
        public string OutputS3Bucket { get; set; }

        [Option(Required = false, HelpText = "the aws profile used for credentials for uploading tileset")]
        public string AWSProfile { get; set; }

        [Option(Required = false, Default = "us-gov-west-1", HelpText = "the aws endpoint for the destination tileset bucket")]
        public string AWSRegion { get; set; }

        // observation filtering related (landform standard)
        [Option(HelpText = "Allowed sources for adjusted transforms, comma separated, all if empty (Adjusted,Manual,Landform,LandformBEV,Agisoft)", Default = null)]
        public string AdjustedTransformSources { get; set; }

        [Option(HelpText = "Allowed sources for transform priors, comma separated, all if empty (Prior,PlacesDB,LocationsDB,PDS)", Default = null)]
        public string PriorTransformSources { get; set; }

        [Option(HelpText = "Use transform priors only", Default = false)]
        public bool UsePriors { get; set; }

        [Option(HelpText = "Use adjusted transforms only", Default = false)]
        public bool OnlyAligned { get; set; }

        // debug related
        [Option(HelpText = "delete existing output before running", Default = false)]
        public bool Redo { get; set; }
    }

    public class LocalConvertToLegacyScene : LandformCommand
    {
        private LocalConvertToLegacySceneOptions options;

        private MissionSpecific mission;
        private RoverMasker masker;

        private string outputPath;

        public LocalConvertToLegacyScene(LocalConvertToLegacySceneOptions options) : base(options)
        {
            this.options = options;
        }

        public int Run()
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();

            if (options.UsePriors && options.OnlyAligned)
            {
                pipeline.LogError("cannot specify both --usepriors and --onlyaligned");
                return 1;
            }

            var project = Project.Find(pipeline, options.ProjectName);
            if (project == null)
            {
                pipeline.LogError("project \"{0}\" not found", options.ProjectName);
                return 1;
            }
            mission = MissionSpecific.GetInstance(project.Mission);
            masker = mission.GetMasker();

            var meshFrame = options.MeshFrame;
            FrameTransform.ParseFrameName(ref meshFrame, out bool specificSiteDrive);
            if (!specificSiteDrive)
            {
                pipeline.LogError("unsupported mesh frame: " + meshFrame + ". ASTTRO wants a sitedrive to be at origin.");
                return 1;
            }

            var adjustedSources = FrameTransform.ParseSources(options.AdjustedTransformSources);
            var priorSources = FrameTransform.ParseSources(options.PriorTransformSources);

            string dir = string.Format("www/{0}", options.ProjectName);
            string inputDir = pipeline.GetLocalDebugFolder(Path.GetDirectoryName(options.InputTileset), dir, options.ProjectName);
            if (!Directory.Exists(inputDir))
            {
                pipeline.LogError("input directory {0} doesn't exist", inputDir);
                return 1;
            }

            pipeline.LogInfo("preparing to convert tileset {0}", options.InputTileset);

            dir = string.Format("scene/legacy/{0}Frame", options.MeshFrame);
            dir = FrameTransform.AppendSourcesPath(dir, adjustedSources, priorSources, options.UsePriors);
            outputPath = pipeline.GetLocalDebugFolder(options.OutputFolder, dir, options.ProjectName);
            if (options.Redo && Directory.Exists(outputPath))
            {
                Directory.Delete(outputPath, true);
            }
            PathHelper.EnsureExists(outputPath);

            pipeline.LogInfo("results will be written to {0}", outputPath);

            var frameCache = new FrameCache(pipeline, options.ProjectName);
            frameCache.PreloadFilteredTransforms(priorSources, adjustedSources, options.UsePriors);

            string imageObs = ObservationType.Image.ToString();
            string maskObs = ObservationType.RoverMask.ToString();
            var observationCache = new ObservationCache(pipeline, options.ProjectName);
            observationCache.
                Preload(obs => obs.UseForReconstruction &&
                        (obs.ObservationType == imageObs || obs.ObservationType == maskObs));

            var imageObservations = observationCache.GetAllObservations().Where(obs => obs.ObservationType == imageObs).ToList();

            pipeline.LogInfo("Building legacy scene for ASTTRO");
            string manifestPath = null;
            {
                var RASLRecords =
                    imageObservations.Select(x => new EmtToScene.FileRecord(new System.Uri(x.Url).LocalPath));
                EmtToScene.CreateLegacyScene(RASLRecords, outputPath, out manifestPath, meshFrame);
            }

            pipeline.LogInfo("rotating meshes to ASTTRO frame");
            CoreLimitedParallel.ForEach(Directory.EnumerateFiles(inputDir, options.MeshExtension), inputFilePath =>
            {
                // read every mesh in and convert to asttro coordinate frame
                pipeline.LogInfo("Transforming data: {0}", inputFilePath);
                Mesh mesh = Mesh.Load(inputFilePath);
                EmtToScene.ConvertMeshToYUp(mesh);
                string outputFilePath = Path.Combine(outputPath, Path.GetFileName(inputFilePath));
                mesh.Save(outputPath);
            }
            );

            pipeline.LogInfo("modifying tileset json");
            {
                string jsonData = File.ReadAllText(options.InputTileset);
                string outputJsonPath = Path.Combine(outputPath, Path.GetFileName(options.InputTileset));
                //HACK: Issue #602: to emulate the previous version of tilest.json that asttro uses
                jsonData = jsonData.Replace("uri", "url");

                //TODO: rotate all bounds 

                File.WriteAllText(outputJsonPath, jsonData);
            }

            // create master manifest
            pipeline.LogInfo("Creating master manifest");
            CreateMasterManifest(meshFrame, outputPath, manifestPath, imageObservations);

            pipeline.LogInfo("converted to legacy scene successfully");

            // TODO: upload to s3
            return 0;
        }

        private void CreateMasterManifest(string outputFrame, string astroOutputPath, string manifestPath,
                                         IEnumerable<Observation> imageObservations)
        {
            pipeline.LogInfo("Building master manifest");
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
            CreateMasterManifest(LocalPathToS3Url(astroOutputPath, manifestPath),
                                 Path.Combine(astroOutputPath, "mastermanifest.xml"),
                                 outputFrame, numNavcams, numMastcams);
        }

        string LocalPathToS3Url(string localRoot, string localPath)
        {
            string relativePath =
                StringHelper.NormalizeSlashes(localPath.Substring(Path.GetDirectoryName(localRoot).Length + 1));
            return StringHelper.NormalizeUrl(options.OutputS3Bucket + relativePath, "s3://", false);
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
