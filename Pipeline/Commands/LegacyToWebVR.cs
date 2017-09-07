using CommandLine;
using log4net;
using Microsoft.Xna.Framework;
using OPS.Geometry;
using OPS.Imaging;
using System;
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

        public int Extent { get; set; }
    }

    public class LegacyToWebVR
    {
        private static readonly ILog logger = LogManager.GetLogger(typeof(LegacyToWebVR));

        private LegacyToWebVROptions options;

        public LegacyToWebVR(LegacyToWebVROptions opts)
        {
            options = opts;
        }

        public int Run()
        {

            LegacyScene scene = new LegacyScene(options.InputDirectory, options.Extent);
            //foreach (var leaf in scene.TerrainRoot.Leaves())
            //{
            //    var tmp = leaf.GetComponent<MeshImagePair>();
            //    string filenameRoot = Path.Combine(options.OutpitDirectory, leaf.Name);
            //    tmp.Image.Save<byte>(filenameRoot + ".jpg");
            //    tmp.Mesh.Save(filenameRoot + ".obj", filenameRoot + ".jpg");
            //}

            SceneNode root = new SceneNode("");
            root.Bounds = new BoundingBox(new Vector3(-500, double.MinValue, 500), new Vector3(500, double.MaxValue, 500));

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
            // Make walkabe tiles starting slide 8
            for (int x = 0; x < 4; x++)
            {
                for (int y = 0; y < 4; y++)
                {
                    Split(sixteen[x, y]);
                }
            }

            foreach (var leaf in root.Leaves())
            {
                int textureWidth = 512;
                int textureHeight = 512;



                var overlaps = FindOverlappingLeaves(leaf, scene.TerrainRoot);
                var meshes = overlaps.Select(x => x.GetComponent<MeshImagePair>().Mesh).ToArray();
                // TODO: Remove skirts
                var m = Mesh.Merge(meshes);
                m = MeshLab.ResampleDecimation(m);
                m = UVAtlas.Atlas(m, textureWidth, textureHeight);
                m = Mesh.Clip(m, leaf.Bounds);
               
                var pairs = overlaps.Select(x => x.GetComponent<MeshImagePair>());
                var img = TextureBaker.BakeTexture(pairs.ToArray(), m, textureWidth, textureHeight);
                // TODO: Add skirts
                leaf.Bounds = m.Bounds();
                // TODO: offset leaf
                leaf.AddComponent(new MeshImagePair(m, img));
            }
            // TODO: bake parent tiles

            foreach (var tile in root.DepthFirstTraverse())
            {
                var pair = tile.GetComponent<MeshImagePair>();
                if (pair != null)
                {
                    string name = Path.Combine(options.OutpitDirectory, tile.Name);
                    string imgName = name + ".jpg";
                    pair.Image.Save<byte>(imgName);
                    pair.Mesh.Save(name + ".ply", imgName);
                }
            }
            return 0;
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

            //  X ---> 
            // Y  0 1
            // |  2 3
            
            SceneNode n0 = new SceneNode(parent.Name + "0", parent.Transform);
            n0.Bounds = new BoundingBox(new Vector3(min.X, double.MinValue, min.Z), new Vector3(center.X, double.MaxValue, center.Z));

            SceneNode n1 = new SceneNode(parent.Name + "1", parent.Transform);
            n1.Bounds = new BoundingBox(new Vector3(center.X, double.MinValue, min.Z), new Vector3(max.X, double.MaxValue, center.Z));

            SceneNode n2 = new SceneNode(parent.Name + "2", parent.Transform);
            n2.Bounds = new BoundingBox(new Vector3(min.X, double.MinValue, center.Z), new Vector3(center.X, double.MaxValue, max.Z));

            SceneNode n3 = new SceneNode(parent.Name + "3", parent.Transform);
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
