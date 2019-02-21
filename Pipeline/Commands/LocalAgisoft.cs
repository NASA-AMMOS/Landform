using System;
using System.Linq;
using System.Xml;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Collections;
using CommandLine;
using log4net;
using OPS.Util;
using OPS.Pipeline.AlignmentServer;
using OPS.Imaging;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using OPS.Alignment;
using OPS.Geometry;
namespace OPS.Pipeline
{
    [Verb("local-agisoft", HelpText = "run agisoft on ingested images")]
    public class LocalAgisoftOptions : PipelineCoreOptions
    {
        [Value(0, Required = true, HelpText = "project name", Default = null)]
        public string ProjectName { get; set; }
    }

    class LocalAgisoft : LocalPipeline
    {
        private LocalAgisoftOptions options;

        public LocalAgisoft(LocalAgisoftOptions options) : base(options)
        {
            this.options = options;
        }
        private void AddAttributeXml(XmlNode node, string name, string value)
        {
            XmlAttribute att = node.OwnerDocument.CreateAttribute(name);
            att.Value = value;
            node.Attributes.Append(att);
        }

        private void AddSensorXml(XmlNode sensorsNode, int sensorId, RoverProductCamera roverProdCam, int widthPixels, int heightPixels)
        {
            XmlNode sensorNode = sensorsNode.OwnerDocument.CreateElement("sensor");
            sensorsNode.AppendChild(sensorNode);
            AddAttributeXml(sensorNode, "id", sensorId.ToString());
            AddAttributeXml(sensorNode, "label", roverProdCam.ToString()+ "_" + widthPixels + "_" + heightPixels);

            if (roverProdCam.ToString().Contains("Hazcam"))
                throw new NotImplementedException("hazcams may need a fisheye camera type set here");
            AddAttributeXml(sensorNode, "type", "frame");

            XmlNode resolutionNode = sensorNode.OwnerDocument.CreateElement("resolution");
            sensorNode.AppendChild(resolutionNode);
            AddAttributeXml(resolutionNode, "width", widthPixels.ToString());
            AddAttributeXml(resolutionNode, "height", heightPixels.ToString());

            XmlNode propNode1 = sensorNode.OwnerDocument.CreateElement("property");
            sensorNode.AppendChild(propNode1);
            AddAttributeXml(propNode1, "name", "pixel_width");
            AddAttributeXml(propNode1, "value", PDSParser.GetSensorPixelSizeMM(roverProdCam).ToString("F3"));

            XmlNode propNode2 = sensorNode.OwnerDocument.CreateElement("property");
            sensorNode.AppendChild(propNode2);
            AddAttributeXml(propNode2, "name", "pixel_height");
            AddAttributeXml(propNode2, "value", PDSParser.GetSensorPixelSizeMM(roverProdCam).ToString("F3"));

            XmlNode propNode3 = sensorNode.OwnerDocument.CreateElement("property");
            sensorNode.AppendChild(propNode3);
            AddAttributeXml(propNode3, "name", "focal_length");
            AddAttributeXml(propNode3, "value", PDSParser.GetFocalLengthMM(roverProdCam).ToString("F2"));

            XmlNode propNode4 = sensorNode.OwnerDocument.CreateElement("property");
            sensorNode.AppendChild(propNode4);
            AddAttributeXml(propNode4, "name", "layer_index");
            AddAttributeXml(propNode4, "value", "0");

            XmlNode bandsNode = sensorNode.OwnerDocument.CreateElement("bands");
            sensorNode.AppendChild(bandsNode);
            if (roverProdCam.ToString().Contains("Mastcam") || roverProdCam.ToString().Contains("MAHLI"))
                throw new NotImplementedException("color images may need more bands");
            XmlNode bandNode = sensorNode.OwnerDocument.CreateElement("band");
            bandsNode.AppendChild(bandNode);

            XmlNode dataTypeNode = sensorNode.OwnerDocument.CreateElement("data_type");
            sensorNode.AppendChild(dataTypeNode);
            dataTypeNode.InnerText = "uint8";

            XmlNode calibNode = sensorNode.OwnerDocument.CreateElement("calibration");
            sensorNode.AppendChild(calibNode);
            if (roverProdCam.ToString().Contains("Hazcam"))
                throw new NotImplementedException("hazcams may need a fisheye camera type set here");
            AddAttributeXml(calibNode, "type", "frame");
            AddAttributeXml(calibNode, "class", "initial");

            XmlNode resNode = sensorNode.OwnerDocument.CreateElement("resolution");
            calibNode.AppendChild(resNode);
            AddAttributeXml(resNode, "width", widthPixels.ToString());
            AddAttributeXml(resNode, "height", heightPixels.ToString());

            XmlNode fNode = sensorNode.OwnerDocument.CreateElement("f");
            calibNode.AppendChild(fNode);
            double focalLengthPixels = PDSParser.GetFocalLengthMM(roverProdCam) / PDSParser.GetSensorPixelSizeMM(roverProdCam);
            fNode.InnerText = focalLengthPixels.ToString("F1");

            XmlNode blackLevelNode = sensorNode.OwnerDocument.CreateElement("black_level");
            sensorNode.AppendChild(blackLevelNode);
            blackLevelNode.InnerText = "0.0000000000000000e+000";

            XmlNode sensitivityNode = sensorNode.OwnerDocument.CreateElement("sensitivity");
            sensorNode.AppendChild(sensitivityNode);
            sensitivityNode.InnerText = "1.0000000000000000e+000";
        }

        public int Run()
        {
           
            var project = Project.Find(this, options.ProjectName);
            if (project == null)
            {
                LogError("project \"{0}\" not found", options.ProjectName);
                return 1;
            }

            var imageDir = TemporaryFile.GetTempSubdir("images");
            var masksDir = TemporaryFile.GetTempSubdir("masks");
            var agiScratch = TemporaryFile.GetTempSubdir("agiScratch");

            //TODO: clear directories

            this.LogInfo("building scene graph for bundle adjustment, project {0}", options.ProjectName);
            var bsg = new BuildSceneGraph(this, project.Name, new BuildSceneGraph.Options
            {
                UseTransformPriors = true,
                LoadCorrespondences = false,
                OnlyKeepImagesWithFeatures = false,
                OnlyKeepBestImages = true,
                OnlyCrossSiteDriveOverlaps = false,
                IncludeObservation = o => o.ObservationType == ObservationType.Image.ToString() && o.UseForReconstruction
            });

            AlignmentScene scene = bsg.BuildTopDown(project.RootFrame);
         
            var observations = scene.Root.GetComponentsInTree<NodeObservation>().Select(no => no.Observation as RoverObservation);
            var obsByCameraConfig = observations.GroupBy(o => new { o.Sensor, o.Width, o.Height });
            var siteDrives = observations.Select(o => new SiteDrive(o.Site, o.Drive)).Distinct().OrderBy(sd => sd);

            //add xml fragmentes needed for cameras
            XmlDocument doc = new XmlDocument();
            doc.AppendChild(doc.CreateXmlDeclaration("1.0", "UTF-8", null));
            XmlNode docNode = doc.CreateElement("document");
            doc.AppendChild(docNode);
            AddAttributeXml(docNode, "version", "1.5.0");
           
            XmlNode chunkNode = doc.CreateElement("chunk");
            docNode.AppendChild(chunkNode);
            AddAttributeXml(chunkNode, "label", "Chunk 1");
            AddAttributeXml(chunkNode, "enabled", "1");

            XmlNode sensorsNode = doc.CreateElement("sensors");
            chunkNode.AppendChild(sensorsNode);
            AddAttributeXml(sensorsNode, "next_id", obsByCameraConfig.Count().ToString());

            XmlNode camerasNode = doc.CreateElement("cameras");
            chunkNode.AppendChild(camerasNode);
            AddAttributeXml(camerasNode, "next_id", observations.Count().ToString());
            AddAttributeXml(camerasNode, "next_group_id", "0");

            //add sensors and cameras
            int sensorId = 0;
            int cameraId = 0;
            foreach(var cameraConfig in obsByCameraConfig)
            {
                RoverProductCamera roverProdCam = (RoverProductCamera)Enum.Parse(typeof(RoverProductCamera),cameraConfig.Key.Sensor);
                AddSensorXml(sensorsNode, sensorId, roverProdCam, cameraConfig.Key.Width, cameraConfig.Key.Height);
                          
                foreach (var obs in cameraConfig)
                {
                    Image img = this.LoadImage(obs.Url);
                    img.Save<byte>(Path.Combine(imageDir, obs.Name + ".png"));

                    Image mask = this.GetDataProduct<PngDataProduct>(project.ProductPath, obs.MaskGuid, project.Name).Image;
                    mask.Save<byte>(Path.Combine(masksDir, obs.Name + "_mask.png"));

                    SceneNode node = scene.ObservationUrlToNode[obs.Url];
                    Matrix cameraToRootRowVectorRightHanded = node.Transform.LocalToWorld;
                    Matrix cameraToRootColVectorRightHanded = Matrix.Transpose(cameraToRootRowVectorRightHanded);
                   
                    AddCameraXml(camerasNode, cameraId, sensorId, obs.Name, cameraToRootColVectorRightHanded); //BUGBUG want inverse?

                    cameraId++;
                }
                
                sensorId++;
            }

            doc.Save(Path.Combine(agiScratch, "Cameras.xml"));

            return 0;
        }

        private void AddCameraXml(XmlNode cameras, int cameraId, int sensorId, string name, Matrix cameraToRootColVec)
        {
            XmlNode cameraNode = cameras.OwnerDocument.CreateElement("camera");
            cameras.AppendChild(cameraNode);
            AddAttributeXml(cameraNode, "id", cameraId.ToString());
            AddAttributeXml(cameraNode, "sensor_id", sensorId.ToString());
            AddAttributeXml(cameraNode, "label", name);
            AddAttributeXml(cameraNode, "enabled", "1");

            XmlNode transformNode = cameras.OwnerDocument.CreateElement("transform");
            cameraNode.AppendChild(transformNode);
            transformNode.InnerText = cameraToRootColVec.M11.ToString("e16") + " " + cameraToRootColVec.M12.ToString("e16") + " " + cameraToRootColVec.M13.ToString("e16") + " " + cameraToRootColVec.M14.ToString("e16") + " " +
                cameraToRootColVec.M21.ToString("e16") + " " + cameraToRootColVec.M22.ToString("e16") + " " + cameraToRootColVec.M23.ToString("e16") + " " + cameraToRootColVec.M24.ToString("e16") + " " +
                cameraToRootColVec.M31.ToString("e16") + " " + cameraToRootColVec.M32.ToString("e16") + " " + cameraToRootColVec.M33.ToString("e16") + " " + cameraToRootColVec.M34.ToString("e16") + " " +
                cameraToRootColVec.M41.ToString("e16") + " " + cameraToRootColVec.M42.ToString("e16") + " " + cameraToRootColVec.M43.ToString("e16") + " " + cameraToRootColVec.M44.ToString("e16");
        }
    }
}
