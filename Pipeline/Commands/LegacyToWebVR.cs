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

        [Option(Required = false, Default = 2048, HelpText = "Maxium texture size for a tile")]
        public int MaxTextureSize { get; set; }

        [Option(Required = false, Default = 2000, HelpText = "Number of allowed faces per tile")]
        public int FacesPerTile { get; set; }

        [Option(Required = false, Default = true, HelpText = "Only process the inner most set of tiles")]
        public bool InnerMostTilesOnly { get; set; }
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

        public int Run()
        {
            PathHelper.EnsureExists(options.OutpitDirectory);
            LegacyScene scene = new LegacyScene(options.InputDirectory, options.InputExtent);
            //foreach (var leaf in scene.TerrainRoot.Leaves())
            //{
            //    var tmp = leaf.GetComponent<MeshImagePair>();
            //    string filenameRoot = Path.Combine(options.OutpitDirectory, leaf.Name);
            //    tmp.Image.Save<byte>(filenameRoot + ".jpg");
            //    tmp.Mesh.Save(filenameRoot + ".obj", filenameRoot + ".jpg");
            //}

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
                var m = Mesh.Merge(meshes);
                m.Clean();
                m = MeshLab.ResampleDecimation(m, targetFaces: options.FacesPerTile);
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

            Image img = (Image) pair.Image.Clone();
            string name = Path.Combine(options.OutpitDirectory, tile.Name);


            int baseSize = img.Width;
            int sizeLG = Math.Min(options.MaxTextureSize, baseSize);
            int sizeMD = Math.Min(options.MaxTextureSize / 2, baseSize);
            int sizeSM = sizeMD / 2;
            int sizeXSM = Math.Max(64, sizeSM / 2);

            TextureSize ts = new TextureSize(sizeMD, false);
            string imgName = name + ".jpg";
            if (sizeLG == baseSize)
            {
                img.Save<byte>(name + "_lg.jpg");
                imgName = name + "_lg.jpg";
                ts.lg = true;
            }
            if (baseSize != sizeMD)
            {
                img = img.ResizeSimpleBicubic(sizeMD, sizeMD);
            }
            img.Save<byte>(name + ".jpg");
            img = img.ResizeSimpleBicubic(sizeSM, sizeSM);
            img.Save<byte>(name + "_sm.jpg");
            img = img.ResizeSimpleBicubic(sizeXSM, sizeXSM);
            img.Save<byte>(name + "_xsm.jpg");


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
                    }
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
                        p.Bounds = BoundingBoxExtensions.Union(p.Children.Select(c => c.Bounds).ToList());
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
