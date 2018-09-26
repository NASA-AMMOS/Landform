using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.Xna.Framework;
using OPS.Geometry;
using OPS.Imaging;
using OPS.Util;

namespace OPS.Pipeline
{

    

    public class LegacySceneManfiest
    {
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
            public Quaternion RoverOriginRotation;
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
            originEl.InnerText = p.FirstLine + "," + p.FirstSample;
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
            firstLineEl.InnerText = p.FirstLine.ToString();
            firstLineSampleEl.InnerText = p.FirstSample.ToString();
            dimEl.AppendChild(widthEl);
            dimEl.AppendChild(heightEl);
            dimEl.AppendChild(firstLineEl);
            dimEl.AppendChild(firstLineSampleEl);
            dimEl.AppendChild(originEl);

            Quaternion qvals = siteData.RoverOriginRotation;
            XmlElement rotationQuaternion = doc.CreateElement("rover_rotation");
            rotationQuaternion.InnerText = string.Format("{0} {1} {2} {3}", qvals.W, qvals.X, qvals.Y, qvals.Z);
            imageEl.AppendChild(rotationQuaternion);

            fov.InnerText = string.Format("{0}", p.HorizontalFOV);
            sitedrive.InnerText = siteData.SiteDrive.ToString();
            
            imageEl.AppendChild(dimEl);
            imageEl.AppendChild(fov);
            imageEl.AppendChild(sitedrive);

            var cm = imageData.Metadata.CameraModel;
            if (cm.GetType() == typeof(CAHV))
            {
                XmlElement camModelEl = CAHVToXml(doc, (CAHV)cm);
                imageEl.AppendChild(camModelEl);
            } else
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

        protected XmlElement CreateVectorElement(XmlDocument doc, Vector3 vec, string elementName)
        {
            XmlElement el = doc.CreateElement(elementName);
            el.InnerText = string.Join(",", vec.X, vec.Y, vec.Z);
            return el;
        }


        public static void BuildFromDirectory(string dir)
        {
            string indir = dir;
            string outdir = Path.Combine(indir, "output");
            PathHelper.EnsureExists(outdir);
            var manifest = new LegacySceneManfiest();
            Dictionary<string, LegacySceneManfiest.SiteDriveData> siteDriveLookup = new Dictionary<string, LegacySceneManfiest.SiteDriveData>();

            Serial.ForEach(System.IO.Directory.EnumerateFiles(indir, "*.obj"), f =>
            {

                var imgname = f.Replace(".obj", ".vic");

                // Make metadata
                {
                    var meta = new PDSMetadata(imgname);
                    var p = new PDSParser(meta);

                    if (!siteDriveLookup.ContainsKey(p.SiteDrive))
                    {
                        var sd = new LegacySceneManfiest.SiteDriveData()
                        {
                            SiteDrive = new SiteDrive(p.SiteDrive),
                            Transform = Matrix.Identity,
                            Images = new List<LegacySceneManfiest.ImageData>()
                        };
                        siteDriveLookup.Add(p.SiteDrive, sd);
                        manifest.AddSiteDrive(sd);
                    }
                    var imageData = new LegacySceneManfiest.ImageData()
                    {
                        FileId = Path.GetFileNameWithoutExtension(imgname),
                        Metadata = meta
                    };
                    siteDriveLookup[p.SiteDrive].Images.Add(imageData);
                    //Console.WriteLine(p.Site + " " + p.Drive);
                    //foreach (var g in meta.Groups())
                    //{
                    //    foreach (var k in meta.Keys(g))
                    //    {
                    //        Console.WriteLine(g + "\t" + k + "\t" + meta[g, k]);
                    //    }
                    //}

                }

                //Make meshes
                {
                    string destRoot = Path.Combine(outdir, Path.GetFileNameWithoutExtension(f));
                    Mesh m = Mesh.Load(f);
                    Image img = Image.Load(imgname);
                    img.Save<byte>(destRoot + ".jpg");
                    m.Save(destRoot + ".obj", destRoot + ".jpg");
                }
            });
            siteDriveLookup[siteDriveLookup.Keys.OrderBy(x => x).Last()].Primary = true;
            string content = manifest.Create();
            File.WriteAllText(Path.Combine(outdir, "manifest.xml"), content);
        }
    }
}
