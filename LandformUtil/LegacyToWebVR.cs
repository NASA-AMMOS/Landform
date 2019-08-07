using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;
using log4net;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using OPS.MathExtensions;
using OPS.Util;
using OPS.Imaging;
using OPS.Geometry;
using OPS.RayTrace;

namespace OPS.Pipeline
{
    [Verb("legacytowebvr", HelpText = "Crawl MSL S3 bucket for dataproducts and add them to the landform database")]
    public class LegacyToWebVROptions
    {
        [Value(0, Required = true, HelpText = "Directory containing legacy tiles")]
        public string InputDirectory { get; set; }

        [Value(1, Required = true, HelpText = "Directory to write new tiles to")]
        public string OutputDirectory { get; set; }

        [Option(Required = false, Default = 4096, HelpText = "Total extent of legacy tiles")]
        public int InputExtent { get; set; }

        [Option(Required = false, Default = 4096, HelpText = "Total extent of output tiles")]
        public int OutputExtent { get; set; }

        [Option(Required = false, Default = 2048, HelpText = "Maxium texture size for a tile")]
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

    /// <summary>
    /// A converter to go from legacy scenes to the format used by AccessMars
    /// A description of the AccessMars tile format can be found here
    /// https://docs.google.com/presentation/d/1DvWbSiiLj4oMgJjVCbwsccy1GXCIKCKWBGotuPPJbJI/edit#slide=id.p
    /// </summary>
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

        void RenderOrtho(SceneCaster sc, int imageRes, int demRes, float extent)
        {
            var mat = Matrix.CreateLookAt(new Vector3(0, 100, 0), new Vector3(0, 0, 0), new Vector3(0,0,1));
            Image img = new Image(3, imageRes, imageRes);
            var cam = new OrthographicCameraModel(mat, new Vector2(img.Width, img.Height), extent);
            CoreLimitedParallel.For(0, img.Height, r => {
                for (int c = 0; c < img.Width; c++)
                {
                    Ray ray = cam.Unproject(new Vector2(c, r));
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
            img.Save<byte>(Path.Combine(options.OutputDirectory, "orthoImage.tif"));

            img = new Image(1, demRes, demRes);
            cam = new OrthographicCameraModel(mat, new Vector2(img.Width, img.Height), 40);
            float maxDist = 0;
            CoreLimitedParallel.For(0, img.Height, r =>
            {
                for (int c = 0; c < img.Width; c++)
                {
                    Ray ray = cam.Unproject(new Vector2(c, r));
                    HitData hit = sc.Raycast(ray);
                    if (hit != null)
                    {
                        var pixel = hit.Texture.UVToPixel(hit.UV.Value);
                        img[0, r, c] = (float) hit.Distance;
                        maxDist = Math.Max(maxDist, img[0, r, c]);
                    }
                }
            });
            CoreLimitedParallel.For(0, img.Height, r =>
            {
                for (int c = 0; c < img.Width; c++)
                {
                    var v = img[0, r, c];
                    img[0, r, c] = maxDist - v;
                }
            });
            img.Save<byte>(Path.Combine(options.OutputDirectory, "orthoDEM.tif"));
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

        /// <summary>
        /// Method to non-linearly stretch uvs for the border mesh
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <param name="u"></param>
        /// <param name="v"></param>
        /// <param name="minDist"></param>
        /// <param name="maxDist"></param>
        /// <param name="minUvDist"></param>
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
            PathHelper.EnsureExists(options.OutputDirectory);

            logger.Info("Loading legacy scene");
            LegacyScene scene = new LegacyScene(options.InputDirectory, options.InputExtent);
            logger.Info("Removing skirts");
            CoreLimitedParallel.ForEach(scene.TerrainRoot.Leaves(), node =>
            {
                var pair = node.GetComponent<MeshImagePair>();
                if (pair != null && pair.Mesh != null)
                {
                    pair.Mesh.RemoveSkirt(SkirtMode.Y);
                }
            });

            // Rendering orthos is just for fun, it isn't used by Access Mars
            logger.Info("Rendering Orthos");
            MakeOrthos(scene, 4096, 1024, 20);

            logger.Info("Computing new scene bounds");
            SceneNode root = new SceneNode("");
            double initExtent = options.OutputExtent / 2.0;
            root.GetOrAddComponent<NodeBounds>().Bounds = new BoundingBox(new Vector3(-initExtent, double.MinValue, -initExtent), new Vector3(initExtent, double.MaxValue, initExtent));

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
            BoundingBox innerBounds = BoundingBoxExtensions.Union(innerNodes.Select(n => n.GetOrAddComponent<NodeBounds>().Bounds).ToArray());
            // Compute collision tile
            {
                logger.Info("Creating low poly collision mesh");
                var meshes = innerNodes.SelectMany(leaf => FindOverlappingLeaves(leaf, scene.TerrainRoot)).Select(node => node.GetComponent<MeshImagePair>().Mesh).ToArray();
                var m = Mesh.Merge(false, false, false, meshes);
                m = m.ResampleDecimation(MeshReconMethod.Poisson, 2000, m.Bounds(), new Vector3(0, 1, 0)); 
                m.Clean();
                m = Mesh.Clip(m, innerBounds);
                m.Clean();
                SceneNode n = new SceneNode("simple");
                n.AddComponent(new MeshImagePair(m));
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
                border = border.ResampleDecimation(MeshReconMethod.Poisson, backgroundFaces, border.Bounds(), new Vector3(0, 1, 0));
                border = Mesh.Cut(border, innerBounds);
                border.Clean();

                double maxDist = 0;
                // The distance from the center to the edge of the inner bounding box in the XZ plane.  Should be 64 when run with standard parameters
                double minDist = innerBounds.Size().X / 2;  
                foreach (var v in border.Vertices)
                {
                    maxDist = Math.Max(maxDist, Math.Max(Math.Abs(v.Position.X), Math.Abs(v.Position.Z)));
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
                border.AddSkirt(SkirtMode.Y, 0.25);
                SceneNode background = new SceneNode("background");
                background.AddComponent<MeshImagePair>(new MeshImagePair(border, borderImage));
                WriteTile(background);
            }


            logger.Info("Creating inner tile meshes");
            ConcurrentDictionary<string, TextureSize> textureSizeData = new ConcurrentDictionary<string, TextureSize>();
            IEnumerable<SceneNode> nodesToProcess = options.InnerMostTilesOnly ? innerNodes : root.Leaves();
                        

            CoreLimitedParallel.ForEach(nodesToProcess, leaf =>
            {
                if (File.Exists(Path.Combine(options.OutputDirectory, leaf.Name) + ".obj"))
                {
                    return;
                }

                logger.Info("Processing " +  leaf.Name);
                var overlaps = FindOverlappingLeaves(leaf, scene.TerrainRoot);

                var meshes = overlaps.Select(x => x.GetComponent<MeshImagePair>().Mesh).ToArray();

                var m = Mesh.Merge(meshes);
                m.Clean();

                int targetFaces = options.FacesPerTile;
                if (nameToFaceCount.ContainsKey(leaf.Name))
                {
                    targetFaces = nameToFaceCount[leaf.Name];
                }
                int faces = Math.Min(m.Faces.Count, targetFaces);
                m = m.ResampleDecimation(MeshReconMethod.Poisson, faces, leaf.GetOrAddComponent<NodeBounds>().Bounds, new Vector3(0, 1, 0));
             
                //m = MeshLab.ResampleDecimation(m, numSamples: targetFaces*10, targetFaces: targetFaces);
                //m = Mesh.Clip(m, leaf.Bounds);
                
                var pairs = overlaps.Select(x => x.GetComponent<MeshImagePair>());
               
                int size = SceneNodeTilingExtensions.ComputeParentTileResolution(pairs, leaf.GetOrAddComponent<NodeBounds>().Bounds, options.MaxTextureSize);
                int textureWidth = size;
                int textureHeight = size;
                int beforeVerts = m.Vertices.Count;
                int beforeFaces = m.Faces.Count;
                m = UVAtlas.Atlas(m, textureWidth, textureHeight);
                var img = TextureBaker.BakeTexture(pairs.ToArray(), m, textureWidth, textureHeight);

                m.AddSkirt(SkirtMode.Y);
                leaf.GetOrAddComponent<NodeBounds>().Bounds = m.Bounds();
                leaf.AddComponent(new MeshImagePair(m, img));
                var ts = WriteTile(leaf);
                textureSizeData.TryAdd(leaf.Name, ts);

            });
            File.WriteAllText(Path.Combine(options.OutputDirectory, "index.json"), JsonConvert.SerializeObject(textureSizeData));
            return 0;
        }

        TextureSize WriteTile(SceneNode tile)
        {
            var pair = tile.GetComponent<MeshImagePair>();
            string imgName = null;
            TextureSize ts = new TextureSize();
            string name = Path.Combine(options.OutputDirectory, tile.Name);
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
                if (!curNode.GetOrAddComponent<NodeBounds>().Bounds.Intersects(target.GetOrAddComponent<NodeBounds>().Bounds))
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
            var min = parent.GetOrAddComponent<NodeBounds>().Bounds.Min;
            var max = parent.GetOrAddComponent<NodeBounds>().Bounds.Max;
            var center = parent.GetOrAddComponent<NodeBounds>().Bounds.Center();

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
            n0.GetOrAddComponent<NodeBounds>().Bounds = new BoundingBox(new Vector3(min.X, double.MinValue, min.Z), new Vector3(center.X, double.MaxValue, center.Z));

            SceneNode n1 = new SceneNode(parent.Name + tileNames[1], parent.Transform);
            n1.GetOrAddComponent<NodeBounds>().Bounds = new BoundingBox(new Vector3(center.X, double.MinValue, min.Z), new Vector3(max.X, double.MaxValue, center.Z));

            SceneNode n2 = new SceneNode(parent.Name + tileNames[2], parent.Transform);
            n2.GetOrAddComponent<NodeBounds>().Bounds = new BoundingBox(new Vector3(min.X, double.MinValue, center.Z), new Vector3(center.X, double.MaxValue, max.Z));

            SceneNode n3 = new SceneNode(parent.Name + tileNames[3], parent.Transform);
            n3.GetOrAddComponent<NodeBounds>().Bounds = new BoundingBox(new Vector3(center.X, double.MinValue, center.Z), new Vector3(max.X, double.MaxValue, max.Z));
            SceneNode[,] result = new SceneNode[2, 2];
            result[0, 0] = n0;
            result[1, 0] = n1;
            result[0, 1] = n2;
            result[1, 1] = n3;
            return result;
        }
    }
}
