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

        [Value(1, Required = false, HelpText = "path to the Agisoft Metashape Professional exe", Default = @"C:\Program Files\Agisoft\Metashape Pro\metashape.exe")]
        public string MetaShapeExePath { get; set; }
    }

    public class LocalAgisoft : LocalPipeline
    {
        private LocalAgisoftOptions options;

        public LocalAgisoft(LocalAgisoftOptions options) : base(options)
        {
            this.options = options;
        }

        public int Run()
        {
            // collect project info for db queries
            var project = Project.Find(this, options.ProjectName);
            if (project == null)
            {
                LogError("project \"{0}\" not found", options.ProjectName);
                return 1;
            }

            // prepare data directories
            var imageDir = TemporaryFile.GetTempSubdir("agi_images");
            var masksDir = TemporaryFile.GetTempSubdir("agi_masks");
            var metaDir = TemporaryFile.GetTempSubdir("agi_meta");

            // prepare metadata filenames
            string calibXMLPath = Path.Combine(metaDir, "calibIn.xml");
            string alignPythonPath = Path.Combine(metaDir, "imageAlign.py");
            string debugAgiScene = Path.Combine(metaDir, "scene.psz");
            string outputCamerasXMLPath = Path.Combine(metaDir, "camerasOut.xml");

            //build scene graph from database tables for images and transforms heirarchy
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

            // prepare png versions of images and masks for agisoft
            var observations = scene.Root.GetComponentsInTree<NodeObservation>().Select(no => no.Observation as RoverObservation);
            this.LogInfo("generating pngs for " + observations.Count() + "images and masks");
            foreach (var obs in observations)
            {
                Image img = this.LoadImage(obs.Url);
                img.Save<byte>(Path.Combine(imageDir, obs.Name + ".png"));

                Image mask = this.GetDataProduct<PngDataProduct>(project.ProductPath, obs.MaskGuid, project.Name).Image;
                mask.Save<byte>(Path.Combine(masksDir, obs.Name + "_mask.png"));
            }

            this.LogInfo("preparing calibration information for agisoft");
            AgisoftXML.WriteCalibrationXML(observations, scene.ObservationUrlToNode, calibXMLPath);

            this.LogInfo("generating python script for agisoft to preform alignment");
            AgisoftPython.WriteImageAlignScript(calibXMLPath, imageDir, masksDir, alignPythonPath, debugAgiScene, outputCamerasXMLPath);

            this.LogInfo("running agisoft alignment");
            string arguments = "-r \"" + alignPythonPath + "\"";
            ProgramRunner pr = new ProgramRunner(options.MetaShapeExePath, arguments, captureOutput: true);
            try
            {
                int exitCode = pr.Run();
                if (exitCode != 0)
                { 
                    throw new InvalidProgramException("exited with status " + exitCode);
                }
               
                if(!File.Exists(outputCamerasXMLPath))
                {
                    throw new InvalidProgramException("failed to create output cameras.xml file");
                }

                this.LogInfo("agisoft alignment complete");
            }
            catch (Exception)
            {
                this.LogError(pr.OutputText);
                this.LogError(pr.ErrorText);
            }
            finally
            {
               Directory.Delete(imageDir, true);
               Directory.Delete(masksDir, true);
               Directory.Delete(metaDir, true);
            }

            return 0;
        }
    }

    class AgisoftPython
    {
        static public void WriteImageAlignScript(string calibXMLPath, string imagesDir, string masksDir, string outputPythonFilePath, string outputAgiScenePath, string outputCamerasXMLPath)
        {
            //set up document
            string fc = "import Metashape\n" +
                        "doc = Metashape.app.document\n" +
                        "chunk = doc.addChunk()\n";

            //add images
            string param = "[";
            foreach (var path in Directory.EnumerateFiles(imagesDir, "*.png"))
            {
                param += "\"" + path.Replace(@"\", "/") + "\", ";
            }
            param.TrimEnd(new char[]{ ',',' '});
            param += "]";
            fc += "chunk.addPhotos(" + param + ")\n";

            //load camera calibrations
            fc += "chunk.importCameras(\"" + calibXMLPath.Replace(@"\", "/") + "\")\n";

            //load our masks
            fc += "chunk.importMasks(path = \"" + masksDir.Replace(@"\", "/") + "/{filename}_mask.png\", source = Metashape.MaskSourceFile, operation = Metashape.MaskOperationReplacement, tolerance = 10)\n";

            //do alginment
            fc += "chunk.matchPhotos(accuracy = Metashape.HighAccuracy, generic_preselection = True, reference_preselection = False)\n" +
                  "chunk.alignCameras()\n";

            //save debug scene for inspection
            fc += "doc.save(path = \"" + outputAgiScenePath.Replace(@"\", "/") + "\", chunks = [doc.chunk])\n";
            
            //save the modified camera positions
            fc += "chunk.exportCameras(\"" + outputCamerasXMLPath.Replace(@"\", "/") + "\", format = Metashape.CamerasFormatXML, export_points = False)\n";

            //save out generated python
            File.WriteAllText(outputPythonFilePath, fc);
        }
    }

    class AgisoftXML
    {
        static private void AddCameraXml(XmlNode cameras, int cameraId, int sensorId, string name)
        {
            XmlNode cameraNode = cameras.OwnerDocument.CreateElement("camera");
            cameras.AppendChild(cameraNode);
            AddAttributeXml(cameraNode, "id", cameraId.ToString());
            AddAttributeXml(cameraNode, "sensor_id", sensorId.ToString());
            AddAttributeXml(cameraNode, "label", name);
            AddAttributeXml(cameraNode, "enabled", "1");
        }

        static private void AddAttributeXml(XmlNode node, string name, string value)
        {
            XmlAttribute att = node.OwnerDocument.CreateAttribute(name);
            att.Value = value;
            node.Attributes.Append(att);
        }

        static private void AddSensorXml(XmlNode sensorsNode, int sensorId, RoverProductCamera roverProdCam, int widthPixels, int heightPixels)
        {
            XmlNode sensorNode = sensorsNode.OwnerDocument.CreateElement("sensor");
            sensorsNode.AppendChild(sensorNode);
            AddAttributeXml(sensorNode, "id", sensorId.ToString());
            AddAttributeXml(sensorNode, "label", roverProdCam.ToString() + "_" + widthPixels + "_" + heightPixels);

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

        static public void WriteCalibrationXML(IEnumerable<RoverObservation> observations, Dictionary<string, SceneNode> observationUrlToNode, string outputCalibXML)
        {
            var obsByCameraConfig = observations.GroupBy(o => new { o.Sensor, o.Width, o.Height });

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
            foreach (var cameraConfig in obsByCameraConfig)
            {
                RoverProductCamera roverProdCam = (RoverProductCamera)Enum.Parse(typeof(RoverProductCamera), cameraConfig.Key.Sensor);
                AddSensorXml(sensorsNode, sensorId, roverProdCam, cameraConfig.Key.Width, cameraConfig.Key.Height);

                foreach (var obs in cameraConfig)
                {
                    AddCameraXml(camerasNode, cameraId, sensorId, obs.Name);
                    cameraId++;
                }

                sensorId++;
            }

            doc.Save(outputCalibXML);
        }

        public static Dictionary<string, Matrix> ReadTransforms(string outputCamerasXMLPath)
        {
            Dictionary<string, Matrix> results = new Dictionary<string, Matrix>();
            XmlDocument xd = new XmlDocument();
            xd.Load(outputCamerasXMLPath);
            XmlNode doc = xd.SelectSingleNode("document");
            XmlNode chunk = doc.SelectSingleNode("chunk");
            XmlNode cameras = chunk.SelectSingleNode("cameras");
            foreach (XmlNode camera in cameras.ChildNodes)
            {
                string name = camera.Attributes["label"].Value;
                XmlNode transforms = camera.SelectSingleNode("transform");
                if (transforms != null)
                {
                    Matrix matrix = ReadTransform(transforms.InnerText);
                    results[name] = matrix;
                }
            }

            return results;
        }

        private static Matrix ReadTransform(string innerText)
        {
            string[] terms = innerText.Split(new char[] { ' ' });

            //do transpose while copying data in
            Matrix rowMajor = new Matrix(double.Parse(terms[0]), double.Parse(terms[4]), double.Parse(terms[8]), double.Parse(terms[12]),
                                         double.Parse(terms[1]), double.Parse(terms[5]), double.Parse(terms[9]), double.Parse(terms[13]),
                                         double.Parse(terms[2]), double.Parse(terms[6]), double.Parse(terms[10]), double.Parse(terms[14]),
                                         double.Parse(terms[3]), double.Parse(terms[7]), double.Parse(terms[11]), double.Parse(terms[15]));
            return rowMajor;
        }
    }
}
