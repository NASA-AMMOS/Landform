using CommandLine;
using log4net;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using OPS.Geometry;
using OPS.Imaging;
using OPS.MathExtensions;
using OPS.Util;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OPS.RayTrace;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Pipeline
{
    [Verb("legacytowebvr", HelpText = "Crawl MSL S3 bucket for dataproducts and add them to the landform database")]
    public class LegacyToWebVROptions
    {
        [Value(0, Required = true, HelpText = "Directory containing legacy tiles")]
        public string InputDirectory { get; set; }

        [Value(1, Required = true, HelpText = "Directory to write new tiles to")]
        public string OutpitDirectory { get; set; }

        [Option(Required = false, Default = 4096, HelpText = "Total extent of legacy tiles")]
        public int InputExtent { get; set; }

        [Option(Required = false, Default = 4096, HelpText = "Total extent of output tiles")]
        public int OutputExtent { get; set; }

        [Option(Required = false, Default = 2048, HelpText = "Maxium tewrxture size for a tile")]
        public int MaxTextureSize { get; set; }

        [Option(Required = false, Default = 2000, HelpText = "Number of allowed faces per tile")]
        public int FacesPerTile { get; set; }

        [Option(Required = false, Default = null, HelpText = "If specified, this json file can specify a specific number of faces per tile")]
        public string FaceCountFile { get; set; }

        [Option(Required = false, Default = true, HelpText = "Only process the inner most set of tiles")]
        public bool InnerMostTilesOnly { get; set; }

        [Option(Required = false, Default = "jpg", HelpText = "Export format for textures (examples: jpg or png")]
        public string ImageFormat { get; set; }
    }

    public class LegacyToWebVR
    {
        private static readonly ILog logger = LogManager.GetLogger(typeof(LegacyToWebVR));

        private LegacyToWebVROptions options;

        public LegacyToWebVR(LegacyToWebVROptions opts)
        {
            options = opts;
        }

        struct TextureSize
        {
            public int s;
            public bool lg;

            public TextureSize(int size, bool hasLarge)
            {
                this.s = size;
                this.lg = hasLarge;
            }
        }

        public void RenderOrtho(SceneCaster sc, int imageRes, int demRes, float extent)
        {
            var mat = Matrix.CreateLookAt(new Vector3(0, 100, 0), new Vector3(0, 0, 0), new Vector3(0,0,1));
            Image img = new Image(3, imageRes, imageRes);
            var cam = new OrthographicCameraModel(mat, new Vector2(img.Width, img.Height), extent);
            Parallel.For(0, img.Height, r => {
                for (int c = 0; c < img.Width; c++)
                {
                    Ray ray = cam.ProjectRay(new Vector2(c + 0.5, r + 0.5));
                    HitData hit = sc.Raycast(ray);
                    if (hit != null)
                    {
                        var pixel = hit.Texture.UVToPixel(hit.UV.Value);
                        img[0, r, c] = hit.Texture[0, (int) pixel.Y, (int) pixel.X];
                        img[1, r, c] = hit.Texture[1, (int) pixel.Y, (int) pixel.X];
                        img[2, r, c] = hit.Texture[2, (int) pixel.Y, (int) pixel.X];
                    }
                }
            });
            img.Save<byte>(Path.Combine(options.OutpitDirectory, "orthoImage.tif"));

            img = new Image(1, demRes, demRes);
            cam = new OrthographicCameraModel(mat, new Vector2(img.Width, img.Height), 40);
            float maxDist = 0;
            Parallel.For(0, img.Height, r =>
            {
                for (int c = 0; c < img.Width; c++)
                {
                    Ray ray = cam.ProjectRay(new Vector2(c + 0.5, r + 0.5));
                    HitData hit = sc.Raycast(ray);
                    if (hit != null)
                    {
                        var pixel = hit.Texture.UVToPixel(hit.UV.Value);
                        img[0, r, c] = (float) hit.Distance;
                        maxDist = Math.Max(maxDist, img[0, r, c]);
                    }
                }
            });
            Parallel.For(0, img.Height, r =>
            {
                for (int c = 0; c < img.Width; c++)
                {
                    var v = img[0, r, c];
                    img[0, r, c] = maxDist - v;
                }
            });
            img.Save<byte>(Path.Combine(options.OutpitDirectory, "orthoDEM.tif"));
        }

        void MakeOrthos(LegacyScene scene, int imageRes, int demRes, float extent)
        {
            SceneCaster sc = new SceneCaster();
            foreach (var leaf in scene.TerrainRoot.Leaves())
            {
                var pair = leaf.GetComponent<MeshImagePair>();
                sc.AddMesh(pair.Mesh, pair.Image, Matrix.Identity);
            }
            sc.Build();
            RenderOrtho(sc, imageRes, demRes, extent);
        }


        class FacesPerTileFileEntry
        {
            public int faces = 0;
            public List<string> ids = null;
        }

        Dictionary<string, int> ReadFacesPerTileFile(string filename)
        {
            var entries = JsonConvert.DeserializeObject<List<FacesPerTileFileEntry>>(File.ReadAllText(filename));
            Dictionary<string, int> faceCounts = new Dictionary<string, int>();
            foreach (var e in entries)
            {
                foreach (var id in e.ids)
                {
                    faceCounts.Add(id, e.faces);
                }
            }
            return faceCounts;
        }


        void XYtoUV(double x, double y, out double u, out double v, double minDist, double maxDist, double minUvDist)
        {
            double baseDist = minDist;
            double centerDist = Math.Max(Math.Abs(x), Math.Abs(y));

            double maxVal =  (Math.Log(maxDist) - Math.Log(baseDist));
            double newDist =  (Math.Log(centerDist) - Math.Log(baseDist)) / maxVal;
            double scale = (newDist * (1 - minUvDist) + minUvDist) / centerDist;

            double xp = x * scale,
                yp = y * scale;

            u = (xp + 1) / 2;
            v = 1 - (yp + 1) / 2;
        }


        public int Run()
        {

            Dictionary<string, int> nameToFaceCount = new Dictionary<string, int>();
            if (options.FaceCountFile != null)
            {
                logger.Info("Reading facecount file");
                nameToFaceCount = ReadFacesPerTileFile(options.FaceCountFile);
            }
            PathHelper.EnsureExists(options.OutpitDirectory);

            logger.Info("Loading legacy scene");
            LegacyScene scene = new LegacyScene(options.InputDirectory, options.InputExtent);

            logger.Info("Rendering Orthos");
            MakeOrthos(scene, 4096, 1024, 20);

            //foreach (var leaf in scene.TerrainRoot.Leaves())
            //{
            //    var tmp = leaf.GetComponent<MeshImagePair>();
            //    string filenameRoot = Path.Combine(options.OutpitDirectory, leaf.Name);
            //    tmp.Image.Save<byte>(filenameRoot + ".jpg");
            //    tmp.Mesh.Save(filenameRoot + ".obj", filenameRoot + ".jpg");
            //}

            logger.Info("Computing new scene bounds");
            SceneNode root = new SceneNode("");
            double initExtent = options.OutputExtent / 2.0;
            root.Bounds = new BoundingBox(new Vector3(-initExtent, double.MinValue, -initExtent), new Vector3(initExtent, double.MaxValue, initExtent));

            SceneNode[,] innerFour = Split(root);
            SceneNode[,] sixteen = null;
            for (int lod = 0; lod < 6; lod++)
            {
                sixteen  = SplitIntoSixteen(innerFour);
                innerFour[0, 0] = sixteen[1, 1];
                innerFour[1, 0] = sixteen[2, 1];
                innerFour[0, 1] = sixteen[1, 2];
                innerFour[1, 1] = sixteen[2, 2];
            }

            List<SceneNode> innerNodes = new List<SceneNode>();
            // Make walkabe tiles starting slide 8
            for (int x = 0; x < 4; x++)
            {
                for (int y = 0; y < 4; y++)
                {
                    var sub = Split(sixteen[x, y]);

                    foreach (var n in sub)
                    {
                        innerNodes.Add(n);
                    }
                }
            }
            BoundingBox innerBounds = BoundingBoxExtensions.Union(innerNodes.Select(n => n.Bounds).ToArray());
            // Compute collision tile
            {
                logger.Info("Creating low poly collision mesh");
                var meshes = innerNodes.SelectMany(leaf => FindOverlappingLeaves(leaf, scene.TerrainRoot)).Select(node => node.GetComponent<MeshImagePair>().Mesh).ToArray();
                var m = Mesh.Merge(false, false, false, meshes);
                m = MeshLab.ResampleDecimation(m, 20000, 2000);
                m.Clean();
                m = Mesh.Clip(m, innerBounds);
                m.Clean();
                SceneNode n = new SceneNode("simple");
                n.AddComponent<MeshImagePair>(new MeshImagePair(m));
                WriteTile(n);
            }

            // Compute background tile
            {
                logger.Info("Creating background tile");
                int backgroundFaces = 32000;
                int backgroundResolution = 512;
                HashSet<SceneNode> outterNodes = new HashSet<SceneNode>();
                foreach (var leaf in root.Leaves())
                {
                    if (!innerNodes.Contains(leaf))
                    {
                        var overlaps = FindOverlappingLeaves(leaf, scene.TerrainRoot);
                        foreach (var ol in overlaps)
                        {
                            outterNodes.Add(ol);
                        }
                    }
                }
                

                Mesh border = Mesh.Merge(outterNodes.Select(n => n.GetComponent<MeshImagePair>().Mesh).ToArray());
                border = MeshLab.ResampleDecimation(border, backgroundFaces * 10, backgroundFaces);
                border = Mesh.Cut(border, innerBounds);
                border.Clean();


                double maxDist = 0;
                double minDist = 64;//double.MaxValue;
                foreach (var v in border.Vertices)
                {
                    maxDist = Math.Max(maxDist, Math.Max(Math.Abs(v.Position.X), Math.Abs(v.Position.Z)));
                    //minDist = Math.Min(minDist, Math.Min(Math.Abs(v.Position.X), Math.Abs(v.Position.Z)));
                }
                foreach (var vert in border.Vertices)
                {
                    double u, v;
                    XYtoUV(vert.Position.X, vert.Position.Z, out u, out v, minDist, maxDist, 0.1);
                    vert.UV = new Vector2(u, v);
                }
                border.HasUVs = true;
                var borderImage =
                    TextureBaker.BakeTexture(outterNodes.Select(n => n.GetComponent<MeshImagePair>()).ToArray(), border,
                        backgroundResolution, backgroundResolution);
                border.AddSkirt(SkirtAxis.Y, 0.25);
                SceneNode background = new SceneNode("background");
                background.AddComponent<MeshImagePair>(new MeshImagePair(border, borderImage));
                WriteTile(background);
            }


            logger.Info("Creating inner tile meshes");
            ConcurrentDictionary<string, TextureSize> textureSizeData = new ConcurrentDictionary<string, TextureSize>();
            IEnumerable<SceneNode> nodesToProcess = options.InnerMostTilesOnly ? innerNodes : root.Leaves();
            Parallel.ForEach(nodesToProcess, leaf =>
            {
                //if (File.Exists(Path.Combine(options.OutpitDirectory, leaf.Name + ".obj")))
                //{
                //    return;
                //}
                Console.WriteLine(leaf.Name);

                var overlaps = FindOverlappingLeaves(leaf, scene.TerrainRoot);
 
                var meshes = overlaps.Select(x => x.GetComponent<MeshImagePair>().Mesh).ToArray();
                // TODO: Remove skirts
                foreach (var mesh in meshes)
                {
                    mesh.RemoveSkirt(SkirtAxis.Y);
                }
                var m = Mesh.Merge(meshes);
                m.Clean();

                int targetFaces = options.FacesPerTile;
                if (nameToFaceCount.ContainsKey(leaf.Name))
                {
                    targetFaces = nameToFaceCount[leaf.Name];
                }
                m = MeshLab.ResampleDecimation(m, numSamples: targetFaces*10, targetFaces: targetFaces);
                m = Mesh.Clip(m, leaf.Bounds);
                
                var pairs = overlaps.Select(x => x.GetComponent<MeshImagePair>());

                // Read all overlapping meshes, crop each to the extent of the leaf tile
                // and calculate the area the triangles occupy in units of pixels.  Sum all
                // the areas and round up to nearest power of two to decide size of the new tile
                double totalPixels = 0;
                foreach (var p in pairs)
                {
                    var triangles = Mesh.Clip(p.Mesh, leaf.Bounds).Triangles();
                    foreach (var t in triangles)
                    {
                        Vector3 a = new Vector3(p.Image.UVToPixel(t.V0.UV), 0);
                        Vector3 b = new Vector3(p.Image.UVToPixel(t.V1.UV), 0);
                        Vector3 c = new Vector3(p.Image.UVToPixel(t.V2.UV), 0);
                        var pixelTri = new Triangle(a, b, c);
                        totalPixels += pixelTri.Area();
                    }
                }
                double size = Math.Sqrt(totalPixels);
                size = MathE.CeilPowerOf2(size);
                size = Math.Min(size, options.MaxTextureSize);

                int textureWidth = (int)size;
                int textureHeight = (int)size;

                m = UVAtlas.Atlas(m, textureWidth, textureHeight);
                var img = TextureBaker.BakeTexture(pairs.ToArray(), m, textureWidth, textureHeight);

                // TODO: Add skirts
                m.AddSkirt(SkirtAxis.Y);
                leaf.Bounds = m.Bounds();
                // TODO: offset leaf
                leaf.AddComponent(new MeshImagePair(m, img));
                var ts = WriteTile(leaf);
                textureSizeData.TryAdd(leaf.Name, ts);

            });
            File.WriteAllText(Path.Combine(options.OutpitDirectory, "index.json"), JsonConvert.SerializeObject(textureSizeData));
            
            // TODO: bake parent tiles
            return 0;
        }

        TextureSize WriteTile(SceneNode tile)
        {
            var pair = tile.GetComponent<MeshImagePair>();

            string imgName = null;
            TextureSize ts = new TextureSize();
            string name = Path.Combine(options.OutpitDirectory, tile.Name);
            if (pair.Image != null)
            {
                Image img = (Image) pair.Image.Clone();
                int baseSize = img.Width;
                int sizeLG = Math.Min(options.MaxTextureSize, baseSize);
                int sizeMD = Math.Min(options.MaxTextureSize / 2, baseSize);
                int sizeSM = sizeMD / 2;
                int sizeXSM = Math.Max(64, sizeSM / 2);

                ts = new TextureSize(sizeMD, false);
                imgName = name + "." + options.ImageFormat;
                if (sizeLG == baseSize)
                {
                    img.Save<byte>(name + "_lg." + options.ImageFormat);
                    imgName = name + "_lg." + options.ImageFormat;
                    ts.lg = true;
                }
                if (baseSize != sizeMD)
                {
                    img = img.ResizeSimpleBicubic(sizeMD, sizeMD);
                }
                img.Save<byte>(name + "." + options.ImageFormat);
                img = img.ResizeSimpleBicubic(sizeSM, sizeSM);
                img.Save<byte>(name + "_sm." + options.ImageFormat);
                img = img.ResizeSimpleBicubic(sizeXSM, sizeXSM);
                img.Save<byte>(name + "_xsm." + options.ImageFormat);

            }
            pair.Mesh.Save(name + ".obj", imgName);
            DracoSerializer.SaveMesh(pair.Mesh, name + ".drc", 12, 10, 4, compressionLevel: 5);
            return ts;
        }

        List<SceneNode> FindOverlappingLeaves(SceneNode target, SceneNode root)
        {
            List<SceneNode> overlaps = new List<SceneNode>();
            Queue<SceneNode> searchList = new Queue<SceneNode>();
            searchList.Enqueue(root);
            while (searchList.Count > 0)
            {
                SceneNode curNode = searchList.Dequeue();
                if (!curNode.Bounds.Intersects(target.Bounds))
                {
                    continue;
                }
                if (curNode.IsLeaf)
                {
                    overlaps.Add(curNode);
                }
                foreach (var c in curNode.Children)
                {
                    searchList.Enqueue(c);
                }
            }
            return overlaps;
        }

        SceneNode[,] SplitIntoSixteen(SceneNode[,] innerFour)
        {
            SceneNode[,] sixteen = new SceneNode[4,4];
            for (int x = 0; x <= 1; x++)
            {
                for (int y = 0; y <= 1; y++)
                {
                    SceneNode[,] nodes = Split(innerFour[x, y]);
                    int sx = x * 2;
                    int sy = y * 2;
                    sixteen[sx, sy] = nodes[0, 0];
                    sixteen[sx + 1, sy] = nodes[1, 0];
                    sixteen[sx, sy + 1] = nodes[0, 1];
                    sixteen[sx + 1, sy + 1] = nodes[1, 1];
                }
            }
            return sixteen;
        }

        SceneNode[,] Split(SceneNode parent)
        {
            var min = parent.Bounds.Min;
            var max = parent.Bounds.Max;
            var center = parent.Bounds.Center();


            bool topDown = false;
            string[] tileNames;
            if (topDown)
            {
                //  X ---> 
                // Y  0 1
                // |  2 3
                tileNames = new string[] {"0", "1", "2", "3"};
            }
            else
            {
                //  X ---> 
                // Y  1 0
                // |  3 2
                tileNames = new string[] { "1", "0", "3", "2" };
            }

            
            SceneNode n0 = new SceneNode(parent.Name + tileNames[0], parent.Transform);
            n0.Bounds = new BoundingBox(new Vector3(min.X, double.MinValue, min.Z), new Vector3(center.X, double.MaxValue, center.Z));

            SceneNode n1 = new SceneNode(parent.Name + tileNames[1], parent.Transform);
            n1.Bounds = new BoundingBox(new Vector3(center.X, double.MinValue, min.Z), new Vector3(max.X, double.MaxValue, center.Z));

            SceneNode n2 = new SceneNode(parent.Name + tileNames[2], parent.Transform);
            n2.Bounds = new BoundingBox(new Vector3(min.X, double.MinValue, center.Z), new Vector3(center.X, double.MaxValue, max.Z));

            SceneNode n3 = new SceneNode(parent.Name + tileNames[3], parent.Transform);
            n3.Bounds = new BoundingBox(new Vector3(center.X, double.MinValue, center.Z), new Vector3(max.X, double.MaxValue, max.Z));
            SceneNode[,] result = new SceneNode[2, 2];
            result[0, 0] = n0;
            result[1, 0] = n1;
            result[0, 1] = n2;
            result[1, 1] = n3;
            return result;
        }
        


        class LegacyScene
        {
            public SceneNode SkyRoot { get; private set; }
            public SceneNode TerrainRoot { get; private set; }

            public LegacyScene(string inputDirectory, double extent)
            {
                SkyRoot = new SceneNode("sky");
                TerrainRoot = new SceneNode("terrain");

                string meshFilePattern = "*.bob";
                foreach (string filename in Directory.EnumerateFiles(inputDirectory, meshFilePattern))
                {
                    Mesh m = Mesh.Load(filename);
                    Image img = null;
                    if (File.Exists(MeshFilenameToImageFilename(filename)))
                    {
                        img = Image.Load(MeshFilenameToImageFilename(filename));
                    }
                    string id = FileToId(filename);
                    SceneNode root = SkyRoot;
                    if (!IsSkyTile(filename))
                    {
                        Vector3 v = GetUnityOffsetVector(id, extent);
                        m.Translate(v);
                        root = TerrainRoot;
                    }
                    for (int i = 0; i < m.Vertices.Count; i++)
                    {
                        var uv = m.Vertices[i].UV;
                        m.Vertices[i].UV = new Vector2(uv.X, 1.0 - uv.Y);
                        m.Vertices[i].Normal = Vector3.Zero; // Zero out normals since sometimes they are invalid
                    }
                    m.HasNormals = false; // Turn off normals since sometimes they are invalid
                    var node = FindOrCreateNode(id, root);
                    MeshImagePair pair = new MeshImagePair(m, img);
                    node.AddComponent(pair);
                }
                ComputeBounds(SkyRoot);
                ComputeBounds(TerrainRoot);
            }

            void ComputeBounds(SceneNode root)
            {
                HashSet<SceneNode> curParents = new HashSet<SceneNode>();
                foreach (var leaf in root.Leaves())
                {
                    var pair = leaf.GetComponent<MeshImagePair>();
                    leaf.Bounds = pair.Mesh.Bounds();
                    if (leaf.Parent != null)
                    {
                        curParents.Add(leaf.Parent);
                    }
                }

                while (curParents.Count > 0)
                {
                    HashSet<SceneNode> nextParents = new HashSet<SceneNode>();
                    foreach (var p in curParents)
                    {
                        p.Bounds = BoundingBoxExtensions.Union(p.Children.Select(c => c.Bounds).ToArray());
                        if (p.Parent != null)
                        {
                            nextParents.Add(p.Parent);
                        }
                    }
                    curParents = nextParents;
                }
            }

            static SceneNode FindOrCreateNode(string id, SceneNode root)
            {
                SceneNode curParent = root;
                SceneNode child = null;
                for (int i = 1; i <= id.Length; i++)
                {
                    string idPrefix = id.Substring(0, i);
                    child = null;
                    foreach (var c in curParent.Children)
                    {
                        if (c.Name == idPrefix)
                        {
                            child = c;
                            break;
                        }
                    }
                    if (child == null)
                    {
                        child = new SceneNode(idPrefix, curParent.Transform);
                    }
                    curParent = child;
                }
                return child;
            }

            static string ParentID(string id)
            {
                return id.Substring(0, id.Length - 1);
            }

            static string FileToId(string filename)
            {
                return Path.GetFileNameWithoutExtension(filename).Remove(0, 1);
            }

            static bool IsSkyTile(string filename)
            {
                return Path.GetFileName(filename)[0] == 'f';
            }

            static string MeshFilenameToImageFilename(string meshFilename)
            {
                if (IsSkyTile(meshFilename))
                {
                    return Path.Combine(Path.GetDirectoryName(meshFilename), "s" + FileToId(meshFilename) + "h.png");
                }
                else
                {
                    return Path.Combine(Path.GetDirectoryName(meshFilename), "t" + FileToId(meshFilename) + "h.jpg");
                }
            }

            public static Vector3 GetUnityOffsetVector(string id, double totalTerrainExtent)
            {
                Vector3 offset = Vector3.Zero;
                double tileSize = totalTerrainExtent;
                for (int i = 0; i < id.Length; i++)
                {
                    char curNum = id[i];
                    tileSize /= 2;

                    if (curNum == '0' || curNum == '2')
                    {
                        offset.X -= tileSize / 2;
                    }
                    if (curNum == '1' || curNum == '3')
                    {
                        offset.X += tileSize / 2;
                    }
                    if (curNum == '0' || curNum == '1')
                    {
                        offset.Z += tileSize / 2;
                    }
                    if (curNum == '2' || curNum == '3')
                    {
                        offset.Z -= tileSize / 2;
                    }
                }
                return offset;
            }
        }

      
    }
}
