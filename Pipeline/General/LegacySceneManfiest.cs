using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Textr1
using System.Threading.Tasks;
using System.Xml;
using log4net;
using Microsoft.Xna.Framework;
using OPS.Geometry;
using OPS.Imaging;
using OPS.Util;

namespace OPS.Pipeline
{
    public class LegacySceneManfiest
    {

        static ILog logger = LogManager.GetLogger(typeof(LegacySceneManfiest));

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

        public LegacySceneManfiest()
        {

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
            else
            {
                throw new Exception("Not enough cameramodels");
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

        protected XmlElement CreateVectorElement(XmlDocument doc, Vector3 vec, string elementName)
        {
            XmlElement el = doc.CreateElement(elementName);
            el.InnerText = string.Join(",", vec.X, vec.Y, vec.Z);
            return el;
        }


        //public static void BuildFromDirectory(string dir, double? decimationRatio = null)
        //{
        //    string indir = dir;
        //    string sceneDir = Path.Combine(indir, "scene");
        //    string imagesDir = Path.Combine(sceneDir, "images");
        //    string preprocessedDir = Path.Combine(indir, "preprocessed");
        //    PathHelper.EnsureExists(sceneDir);
        //    PathHelper.EnsureExists(imagesDir);
        //    PathHelper.EnsureExists(preprocessedDir);

        //    var manifest = new LegacySceneManfiest();
        //    Dictionary<string, LegacySceneManfiest.SiteDriveData> siteDriveLookup = new Dictionary<string, LegacySceneManfiest.SiteDriveData>();

        //    Serial.ForEach(System.IO.Directory.EnumerateFiles(indir, "*.obj"), f =>
        //    {
        //        // Make metadata
        //        var vicname = f.Replace(".obj", ".vic");
        //        var meta = new PDSMetadata(vicname);
        //        var p = new PDSParser(meta);
        //        {
        //            if (!siteDriveLookup.ContainsKey(p.SiteDrive))
        //            {
        //                var sd = new LegacySceneManfiest.SiteDriveData()
        //                {
        //                    SiteDrive = new SiteDrive(p.SiteDrive),
        //                    Transform = Matrix.Identity,
        //                    Images = new List<LegacySceneManfiest.ImageData>()
        //                };
        //                siteDriveLookup.Add(p.SiteDrive, sd);
        //                manifest.AddSiteDrive(sd);
        //            }
        //            var imageData = new LegacySceneManfiest.ImageData()
        //            {
        //                FileId = Path.GetFileNameWithoutExtension(vicname),
        //                Metadata = meta
        //            };
        //            siteDriveLookup[p.SiteDrive].Images.Add(imageData);

        //        }

        //        //Make meshes
        //        {
        //            //var imgname = f.Replace(".obj", ".rgb");
        //            //var imgname = f.Replace(".obj", ".png");
        //            //string destRoot = Path.Combine(preprocessedDir, Path.GetFileNameWithoutExtension(f));
        //            //Mesh m = Mesh.Load(f);
        //            //if(!m.HasNormals)
        //            //{
        //            //    m.GenerateVertexNormals();
        //            //}
        //            //if(decimationRatio.HasValue)
        //            //{
        //            //    int targetFaces = (int)(m.Faces.Count * decimationRatio.Value);
        //            //    m = EdgeCollapse.QuadricEdgeCollapse(m, targetFaces);
        //            //    m = BaselineAtlaser.AtlasSiteFrameMesh(m, Image.Load(f.Replace(".obj", ".VIC")));
        //            //}
        //            //Console.WriteLine("Origin Offset: " + p.OriginOffset);
        //            //m.Translate(-p.OriginOffset);
        //            //ConvertMeshToYUp(m);
        //            //Console.WriteLine("Normals: " + m.HasNormals);
        //            //Image img = Image.Load(imgname);
        //            //Console.WriteLine("Bands: " + img.Bands);
        //            //img.Save<byte>(destRoot + ".jpg");

        //            //PDSMetadata metadata = new PDSMetadata(f.Replace(".obj", ".VIC"));
        //            //string subDest = Path.Combine(imagesDir, new PDSParser(metadata).SiteDrive, Path.GetFileNameWithoutExtension(f) + ".IMG.jpg");
        //            //PathHelper.EnsureExists(Path.GetDirectoryName(subDest));
        //            //img.Save<byte>(subDest);

        //            //m.Save(destRoot + ".obj", destRoot + ".jpg");
        //        }
        //    });
        //    var primarySiteDrive = siteDriveLookup.Keys.OrderBy(x => x).Last();
        //    siteDriveLookup[primarySiteDrive].Primary = true;
        //    string content = manifest.Create();
        //    string sceneSiteDriveFolder = Path.Combine(sceneDir, Path.Combine("ds" + primarySiteDrive, "201801010000"));
        //    PathHelper.EnsureExists(sceneSiteDriveFolder);
        //    File.WriteAllText(Path.Combine(sceneSiteDriveFolder, "manifest.xml"), content);
        //    string tileDir = Path.Combine(sceneSiteDriveFolder, "tile3d_2.0");
        //    PathHelper.EnsureExists(tileDir);
        //    File.WriteAllText(Path.Combine(tileDir, "tilesetSky.json"), SkyTilesetContent);

        //}

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
