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
using OPS.Pipeline.Alignment;

namespace OPS.Pipeline
{
    [Verb("local-agisoft", HelpText = "run agisoft on ingested images")]
    public class LocalAgisoftOptions : PipelineCoreOptions
    {
        [Value(0, Required = true, HelpText = "project name", Default = null)]
        public string ProjectName { get; set; }

        [Value(1, Required = false, HelpText = "path to the Agisoft Metashape Professional exe", Default = @"C:\Program Files\Agisoft\Metashape Pro\metashape.exe")]
        public string MetaShapeExePath { get; set; }

        [Option(HelpText = "Recreate png images for agisoft", Default = false)]
        public bool RedoImages { get; set; }

        [Option(HelpText = "Redo agisoft alignment", Default = false)]
        public bool RedoAlignment { get; set; }

        [Option(HelpText = "Clear the files generated for/by agisoft", Default = true)]
        public bool ClearAgiInputsOutputs { get; set; }

        [Option(HelpText = "Operate on cloud data", Default = false)]
        public bool Cloud { get; set; }
    }

    public class LocalAgisoft
    {
        private LocalAgisoftOptions options;
        private PipelineCore pipeline;

        public LocalAgisoft(LocalAgisoftOptions options)
        {
            this.options = options;
            if (options.Cloud)
            {
                this.pipeline = new CloudPipeline(options, initQueues: false);
            }
            else
            {
                this.pipeline = new LocalPipeline(options);
            }
        }

        public int Run()
        {
            // collect project info for db queries
            var project = Project.Find(pipeline, options.ProjectName);
            if (project == null)
            {
                pipeline.LogError("project \"{0}\" not found", options.ProjectName);
                return 1;
            }

            //get temp paths
            var imageDir = TemporaryFile.GetTempSubdir("agi_images");
            var masksDir = TemporaryFile.GetTempSubdir("agi_masks");
            var metaDir = TemporaryFile.GetTempSubdir("agi_meta");

            //clear old results
            if (options.ClearAgiInputsOutputs)
            {
                Directory.Delete(imageDir, true);
                Directory.Delete(masksDir, true);
                Directory.Delete(metaDir, true);
            }

            //recreate the directories
            PathHelper.EnsureExists(Path.GetFullPath(imageDir));
            PathHelper.EnsureExists(Path.GetFullPath(masksDir));
            PathHelper.EnsureExists(Path.GetFullPath(metaDir));

            // prepare metadata filenames
            string calibXMLPath = Path.Combine(metaDir, "calibIn.xml");
            string alignPythonPath = Path.Combine(metaDir, "imageAlign.py");
            string debugAgiScene = Path.Combine(metaDir, "scene.psz");
            string outputCamerasXMLPath = Path.Combine(metaDir, "camerasOut.xml");

            //build scene graph from database tables for images and transforms heirarchy
            pipeline.LogInfo("building scene graph for bundle adjustment, project {0}", options.ProjectName);
            var bsg = new BuildSceneGraph(pipeline, project.Name, new BuildSceneGraph.Options
            {
                UseTransformPriors = true,
                LoadCorrespondences = false,
                OnlyKeepImagesWithFeatures = false,
                OnlyKeepBestImages = true,
                OnlyCrossSiteDriveOverlaps = false
            });
            AlignmentScene scene = bsg.BuildTopDown(project.RootFrame);

            // prepare png versions of images and masks for agisoft
            var observationCache = new ObservationCache(pipeline, options.ProjectName);
            observationCache.Preload(obs => obs.UseForReconstruction);

            var observations = scene.Root.GetComponentsInTree<NodeObservation>().Select(no => no.Observation as RoverObservation);
            pipeline.LogInfo("generating pngs for " + observations.Count() + " images and masks");
            string maskStr = ObservationType.RoverMask.ToString();
            Dictionary<RoverObservation, int> observationToNumBands = new Dictionary<RoverObservation, int>();
            foreach (var obs in observations)
            {
                string imgPath = Path.Combine(imageDir, obs.Name + ".png");
                Image img = pipeline.LoadImage(obs.Url);
                if (!File.Exists(imgPath) || options.RedoImages)
                {
                    img.Save<byte>(imgPath);
                }

                string maskPath = Path.Combine(masksDir, obs.Name + "_mask.png");
                if (!File.Exists(maskPath) || options.RedoImages)
                {
                    //check for mission mask products in the tables
                    var maskObs = observationCache.GetAllObservationsForFrame(Frame.Find(pipeline, options.ProjectName, obs.FrameName)).Where(o => o.ObservationType == maskStr).Cast<RoverObservation>().ToList();
                    string maskUrl = null;
                    if (maskObs != null && maskObs.Count() > 0)
                    {
                        maskObs.Sort(MSLProject.RoverObservationComparison);
                        maskUrl = maskObs.First().Url;
                    }

                    Image mask = FeatureDetecting.MakeMask(pipeline, maskUrl, img, obs.Name);
                    mask.Save<byte>(Path.Combine(masksDir, obs.Name + "_mask.png"));
                }

                observationToNumBands.Add(obs, img.Bands);
            }

            pipeline.LogInfo("preparing calibration information for agisoft");
            AgisoftXML.WriteCalibrationXML(observations, scene.ObservationUrlToNode, calibXMLPath, observationToNumBands);

            pipeline.LogInfo("generating python script for agisoft to preform alignment");
            AgisoftPython.WriteImageAlignScript(calibXMLPath, imageDir, masksDir, alignPythonPath, debugAgiScene, outputCamerasXMLPath);

            if (!File.Exists(outputCamerasXMLPath) || options.RedoAlignment)
            {
                pipeline.LogInfo("running agisoft alignment");
                string arguments = "-r \"" + alignPythonPath + "\"";
                ProgramRunner pr = new ProgramRunner(options.MetaShapeExePath, arguments, captureOutput: true);
                try
                {
                    int exitCode = pr.Run();
                    if (exitCode != 0)
                    {
                        throw new InvalidProgramException("exited with status " + exitCode);
                    }

                    if (!File.Exists(outputCamerasXMLPath))
                    {
                        throw new InvalidProgramException("failed to create output cameras.xml file");
                    }

                    pipeline.LogInfo("agisoft alignment complete");
                }
                catch (Exception)
                {
                    pipeline.LogError(pr.OutputText);
                    pipeline.LogError(pr.ErrorText);
                }
            }
            else
            {
                pipeline.LogInfo("Skipping alignment, re-using existing results");
            }

            if (!File.Exists(outputCamerasXMLPath))
                return 1;

            //read results
            Dictionary<string, Matrix> adjCameraToScene = AgisoftXML.ReadTransforms(outputCamerasXMLPath);

            //save to database
            int numAdjustedNodes = adjCameraToScene.Count;
            int n = 1;
            foreach (var obs in observations)
            {
                if (adjCameraToScene.ContainsKey(obs.Name))
                {
                    pipeline.LogInfo("saving transform {0} of {1} adjusted frames", n++, numAdjustedNodes);

                    SceneNode adjNode = scene.ObservationUrlToNode[obs.Url];
                    var frame = adjNode.GetComponent<NodeFrame>().Frame;

                    //agi returns the full transform we need just the rover to sitedrive portion
                    //the implemented approach takes all the adjustment found by agisoft and pushes it into the observations to rover matrix
                    //it does not account for changes to the camera calibration found by agisoft and 
                    //does not attempt to find a best transform for the sitedrive to minimize the adjustments to the observations 
                    //tracked as issues #450, #451
                    Matrix cameraToRoot = adjCameraToScene[obs.Name];

                    RoverCoordinateSystem.GetCameraToRover(adjNode.GetComponent<NodeImage>().CameraModel, out Matrix cameraToRover);
                    Matrix roverToCamera = Matrix.Invert(cameraToRover);
                    Matrix roverToRoot = roverToCamera * cameraToRoot;

                    Matrix rootToSiteDrive = adjNode.Parent.Transform.WorldToLocal;
                    Matrix roverToSiteDrive = roverToRoot * rootToSiteDrive;

                    //TODO propagate transform covariance out of agi xml Issue #367
                    //https://github.jpl.nasa.gov/OnSight/Landform/issues/367
                    var ut = new UncertainRigidTransform(roverToSiteDrive);
                    FrameTransform ft = FrameTransform.FindOrCreate(pipeline, frame, TransformSource.Agisoft, ut);
                    ft.Transform = ut;
                    ft.Save(pipeline);
                    if (frame.AddTransform(ft))
                    {
                        frame.Save(pipeline);
                    }
                }
            }

            return 0;
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
                param.TrimEnd(new char[] { ',', ' ' });
                param += "]";
                fc += "chunk.addPhotos(" + param + ")\n";

                //load camera calibrations
                fc += "chunk.importCameras(\"" + calibXMLPath.Replace(@"\", "/") + "\")\n";

                //load our masks
                fc += "chunk.importMasks(path = \"" + masksDir.Replace(@"\", "/") + "/{filename}_mask.png\", source = Metashape.MaskSourceFile, operation = Metashape.MaskOperationReplacement, tolerance = 10)\n";

                //do alginment
                fc += "chunk.matchPhotos(accuracy = Metashape.HighAccuracy, generic_preselection = True, reference_preselection = False)\n" +
                      "chunk.alignCameras(chunk.cameras)\n";

                //required to keep scene generated in metadata from crashing if you select "look through" on the camera (tech support from agisoft)
                fc += "Metashape.app.document.chunk.buildPoints()\n";

                //second pass after optimizing camera and aligning (improves results in the ui)
                fc += "chunk.optimizeCameras()\n" +
                     "chunk.alignCameras(chunk.cameras)\n";

                //save debug scene for inspection
                fc += "doc.save(path = \"" + outputAgiScenePath.Replace(@"\", "/") + "\", chunks = [doc.chunk])\n";

                //save the modified camera positions
                fc += "chunk.exportCameras(\"" + outputCamerasXMLPath.Replace(@"\", "/") + "\", format = Metashape.CamerasFormatXML, export_points = False)\n";

                //save out generated python
                File.WriteAllText(outputPythonFilePath, fc);
            }
        }

        static void GetSceneToCamera(CameraModel m, Matrix roverToScene, out Matrix sceneToCamera)
        {
            RoverCoordinateSystem.GetCameraToRover(m, out Matrix cameraToRover);
            Matrix cameraToScene = cameraToRover * roverToScene;
            sceneToCamera = Matrix.Invert(cameraToScene);
        }

        class AgisoftXML
        {
            static private void AddCameraXml(XmlNode cameras, int cameraId, int sensorId, string name, Matrix matrix)
            {
                XmlNode cameraNode = cameras.OwnerDocument.CreateElement("camera");
                cameras.AppendChild(cameraNode);
                AddAttributeXml(cameraNode, "id", cameraId.ToString());
                AddAttributeXml(cameraNode, "sensor_id", sensorId.ToString());
                AddAttributeXml(cameraNode, "label", name);
                AddAttributeXml(cameraNode, "enabled", "1");
                XmlNode transformNode = cameras.OwnerDocument.CreateElement("transform");
                cameraNode.AppendChild(transformNode);
                Matrix sceneToCamera = matrix;
                Matrix cameraToScene = Matrix.Invert(sceneToCamera);
                string transformString = string.Format("{0} {1} {2} {3} {4} {5} {6} {7} {8} {9} {10} {11} {12} {13} {14} {15}",
                    cameraToScene[0, 0], cameraToScene[1, 0], cameraToScene[2, 0], cameraToScene[3, 0],
                    cameraToScene[0, 1], cameraToScene[1, 1], cameraToScene[2, 1], cameraToScene[3, 1],
                    cameraToScene[0, 2], cameraToScene[1, 2], cameraToScene[2, 2], cameraToScene[3, 2],
                    cameraToScene[0, 3], cameraToScene[1, 3], cameraToScene[2, 3], cameraToScene[3, 3]);
                transformNode.InnerText = transformString;
            }

            static private void AddAttributeXml(XmlNode node, string name, string value)
            {
                XmlAttribute att = node.OwnerDocument.CreateAttribute(name);
                att.Value = value;
                node.Attributes.Append(att);
            }

            static private void AddSensorXml(XmlNode sensorsNode, int sensorId, RoverProductCamera roverProdCam, int widthPixels, int heightPixels, int bands)
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

                if (bands == 1)
                {
                    XmlNode bandNode = sensorNode.OwnerDocument.CreateElement("band");
                    bandsNode.AppendChild(bandNode);
                }
                else if (bands == 3)
                {
                    XmlNode redBandNode = sensorNode.OwnerDocument.CreateElement("band");
                    bandsNode.AppendChild(redBandNode);
                    AddAttributeXml(redBandNode, "label", "Red");

                    XmlNode greenBandNode = sensorNode.OwnerDocument.CreateElement("band");
                    bandsNode.AppendChild(greenBandNode);
                    AddAttributeXml(greenBandNode, "label", "Green");

                    XmlNode blueBandNode = sensorNode.OwnerDocument.CreateElement("band");
                    bandsNode.AppendChild(blueBandNode);
                    AddAttributeXml(blueBandNode, "label", "Blue");
                }
                else
                {
                    throw new InvalidDataException("expecting only 1 or 3 band images");
                }

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

            private struct RoverObsBands
            {
                public RoverObservation Obs;
                public int Bands;
            };

            static public void WriteCalibrationXML(IEnumerable<RoverObservation> observations, Dictionary<string, SceneNode> observationUrlToNode, string outputCalibXML, Dictionary<RoverObservation, int> obsToBands)
            {
                //collect camera configs
                List<RoverObsBands> roverObsBands = new List<RoverObsBands>();
                foreach (var obs in observations)
                {
                    RoverObsBands robsBand = new RoverObsBands();
                    robsBand.Obs = obs;
                    robsBand.Bands = obsToBands[obs];
                    roverObsBands.Add(robsBand);
                }

                var obsByCameraConfig = roverObsBands.GroupBy(o => new { o.Obs.Sensor, o.Obs.Width, o.Obs.Height, o.Bands });

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
                    AddSensorXml(sensorsNode, sensorId, roverProdCam, cameraConfig.Key.Width, cameraConfig.Key.Height, cameraConfig.Key.Bands);

                    foreach (var obs in cameraConfig.Select(o => o.Obs))
                    {
                        SceneNode node = observationUrlToNode[obs.Url];
                        GetSceneToCamera(node.GetComponent<NodeImage>().CameraModel, node.Transform.LocalToWorld, out Matrix sceneToCamera);
                        AddCameraXml(camerasNode, cameraId, sensorId, obs.Name, sceneToCamera);
                        cameraId++;
                    }

                    sensorId++;
                }

                doc.Save(outputCalibXML);
            }

            internal static Dictionary<string, Matrix> ReadTransforms(string outputCamerasXMLPath)
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
                        Matrix cameraToScene = ReadTransform(transforms.InnerText);
                        results[name] = cameraToScene;
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
}
