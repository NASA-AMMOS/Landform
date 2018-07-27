using CommandLine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using OPS.Geometry;
using OPS.Pipeline;
using OPS.Imaging;
using System.IO;
using log4net;
using Newtonsoft.Json;
using OPS.Pipeline.TileServer;

namespace OPS
{


    [Verb("tilelocalmesh", HelpText = "Generates a 3D tileset locally from a mesh")]
    public class TileLocalMeshOptions
    {
        [Value(0, Required = true, HelpText = "Output directory", Default = null)]
        public string OutputDirectory { get; set; }

        [Value(1, Required = true, HelpText = "Filename of mesh")]
        public string InputMesh { get; set; }

        [Value(2, Required = false, HelpText = "Filename of texture", Default =null)]
        public string InputTexture { get; set; }

        [Option(Required = false, Default = 2000, HelpText = "Target maximum faces per tile")]
        public int TargetFacesPerTile { get; set; }

        [Option(Required = false, Default = 256, HelpText = "Maximum image resolution per tile")]
        public int MaxResolutionPerTile { get; set; }

        [Option(Required = false, Default = TilingScheme.Oct, HelpText = "Tiling scheme")]
        public TilingScheme TilingScheme { get; set; }

        [Option(Required = false, Default = SkirtMode.None, HelpText = "Axis to use as up in quad tree tiling")]
        public SkirtMode SkirtAxis { get; set; }
        
        [Option(Required = false, Default = "b3dm", HelpText = "Mesh Extension")]
        public string MeshExtension { get; set; }

        [Option(Required = false, Default = "jpg", HelpText = "Image Extension")]
        public string ImageExtension { get; set; }

        // TODO:  Add uv atlas option
    }



    public class TileLocalMesh
    {
        private static readonly ILog logger = LogManager.GetLogger(typeof(TileLocalMesh));


        TileLocalMeshOptions options;

        bool SkirtsEnabled { get { return options.SkirtAxis != SkirtMode.None; } }

        public class TilingInput
        {
            public BoundingBox TotalBounds;
            public List<TilingInputDataset> Datasets;
            public TextureBaker TextureBaker;
            public TexturedMeshClipper TexturedMeshClipper;

            public TilingInput()
            {
                this.Datasets = new List<TilingInputDataset>();
                TexturedMeshClipper = new TexturedMeshClipper();
            }

            public void AddDataset(TilingInputDataset dataset)
            {
                if (this.Datasets.Count == 0)
                {
                    TotalBounds = dataset.MeshOperator.Bounds;
                }
                TotalBounds = BoundingBoxExtensions.Union(TotalBounds, dataset.MeshOperator.Bounds);
                this.Datasets.Add(dataset);
                if (dataset.Image != null)
                {
                    this.TexturedMeshClipper.AddMeshImagePair(dataset.Mesh, dataset.Image);
                }
            }

            public void InitTextureBaker()
            {
                var datasets = this.Datasets.Where(d => d.Image != null).Select(d => new MeshImagePair(d.Mesh, d.Image)).ToArray();
                if (datasets.Length > 0)
                {
                    TextureBaker = new TextureBaker(datasets);
                }
            }

            public IEnumerable<BoundingBox> FilterEmptyBounds(IEnumerable<BoundingBox> boxes)
            {
                List<BoundingBox> results = new List<BoundingBox>();
                foreach(var b in boxes)
                {
                    // TODO: add spatial lookup to support large numbers of input meshes
                    foreach(var dataset in Datasets)
                    {
                        if(!dataset.MeshOperator.Empty(b))
                        {
                            results.Add(b);
                            break;
                        }
                    }
                }
                return results;
            }

            public bool ShouldSplit(ITileSplitCriteria splitCriteria, BoundingBox box)
            {
                foreach (var dataset in Datasets)
                {
                    // TODO: support texture based splitting
                    // TODO: add spatial lookup to support large numbers of input meshes
                    if (splitCriteria.ShouldSplit(dataset.MeshOperator, box))
                    {
                        return true;
                    }
                }
                return false;
            }

            public Mesh Clip(BoundingBox box, bool ragged = false)
            {
                var meshes = this.Datasets.Where(d => !d.MeshOperator.Empty(box)).Select(d => d.MeshOperator.Clip(box, ragged)).ToArray();
                var merged =  Mesh.Merge(meshes);
                merged.Clean();
                return merged;
            }

            public MeshImagePair ClipWithTexture(BoundingBox box)
            {
                return TexturedMeshClipper.Clip(box);
            }

            public MeshImagePair BakeTexture(Mesh mesh, int size)
            {
                var box = mesh.Bounds();
                mesh = UVAtlas.Atlas(mesh, size, size);
                var img = TextureBaker.Bake(mesh, size, size);
                return new MeshImagePair(mesh, img);
            }
        }

        public class TilingInputDataset
        {
            public Mesh Mesh;
            public Image Image;
            public MeshOperator MeshOperator;

            public TilingInputDataset(string meshFilename, string imageFilename)
            {
                logger.Info(meshFilename);
                this.Mesh = Mesh.Load(meshFilename);
                
                logger.Info(imageFilename);
                if (imageFilename != null)
                {
                    this.Image = Image.Load(imageFilename);
                }
            }

            public TilingInputDataset(Mesh mesh, Image img)
            {
                this.Mesh = mesh;
                if (!this.Mesh.HasNormals)
                {
                    this.Mesh.GenerateVertexNormals();
                }
                this.MeshOperator = new MeshOperator(this.Mesh, buildUVFaceTree: false, buildVertexTree: !this.Mesh.HasFaces);
                this.Image = img;
            }
        }

        public TileLocalMesh(TileLocalMeshOptions opts)
        {
            this.options = opts;
        }

        public int Run()
        {
            OPS.Util.PathHelper.EnsureExists(options.OutputDirectory);
            logger.Info("Loading Input");
            TilingInput input = new TilingInput();
            input.AddDataset(new TilingInputDataset(options.InputMesh, options.InputTexture));
            logger.Info("Init Texture Baker");
            input.InitTextureBaker();
            ITilingScheme scheme;
            if (options.TilingScheme == TilingScheme.Oct)
            {
                scheme = new BinaryTreeTilingScheme();
            }
            else if (options.TilingScheme == TilingScheme.Bin)
            {
                scheme = new OctreeTilingScheme();
            }
            else
            {
                scheme = new QuadTreeTilingScheme(options.SkirtAxis);
            }
            // TODO: Add image size criteria
            ITileSplitCriteria splitCriteria = new FaceLimitSplitCriteria(options.TargetFacesPerTile);
            logger.Info("Computing tree bounds");
            SceneNode root = BuildBoundsTree(input, scheme, splitCriteria);
            logger.Info("Process leaf nodes");
            ProcessLeafNodes(input, root);
            logger.Info("Generate parents");
            BuildParents(root);
            logger.Info("Generate tileset");
            Tile3DBuilder builder = new Tile3DBuilder(root);
            builder.BuildTileset(NodeToUrl, false);
            string jsonData = JsonConvert.SerializeObject(builder.Tileset, Formatting.None);
            File.WriteAllText(Path.Combine(options.OutputDirectory, "tileset.json"), jsonData);
            return 0;
        }

        string NodeToUrl(SceneNode node)
        {
            return node.Name + ".b3dm";
        }

        public static SceneNode BuildBoundsTree(TilingInput input, ITilingScheme tilingScheme, ITileSplitCriteria splitCriteria)
        {
            SceneNode root = new SceneNode("");
            root.AddComponent(new NodeBounds(input.TotalBounds));
            Queue<SceneNode> queue = new Queue<SceneNode>();
            queue.Enqueue(root);
            while(queue.Count > 0 )
            {
                SceneNode cur = queue.Dequeue();
                var curBounds = cur.GetComponent<NodeBounds>().Bounds;
                if (!input.ShouldSplit(splitCriteria, curBounds))
                {
                    continue;
                }
                var childBounds = tilingScheme.Split(null, curBounds);
                childBounds = input.FilterEmptyBounds(childBounds);
                int counter = 0;
                foreach(var childBound in childBounds)
                {
                    SceneNode child = new SceneNode(cur.Name + counter, cur.Transform);
                    child.AddComponent(new NodeBounds(childBound));
                    queue.Enqueue(child);
                    counter++;
                }                
            }
            root.Name = "root";
            return root;
        }

        void ProcessLeafNodes(TilingInput input, SceneNode root)
        {
            var totalLeafCount = root.Leaves().Count();
            Parallel.ForEach(root.Leaves(), (node, pls, index) =>
            {
                logger.Info(string.Format("Leaf: {0} ({1}/{2})", node.Name, index, totalLeafCount));
                Mesh m = input.Clip(node.GetComponent<NodeBounds>().Bounds);
                Image img = null;
                if(options.InputTexture != null)
                {
                    // TODO: compute correct resolution
                    var pair = input.BakeTexture(m, options.MaxResolutionPerTile);
                    if(pair.Image != null)
                    {
                        img = pair.Image;
                        m = pair.Mesh;
                    }
                }
                if (SkirtsEnabled)
                {
                    m.AddSkirt(options.SkirtAxis);
                    node.GetComponent<NodeBounds>().Bounds = m.Bounds();
                }
                node.AddComponent(new MeshImagePair(m, img));
                node.AddComponent(new NodeGeometricError(0));
                node.SaveMesh(options.OutputDirectory, meshExtension:  options.MeshExtension, imageExtension: options.ImageExtension);
            });
        }

        void BuildParents(SceneNode root)
        {
            var totalLeafCount = root.Leaves().Count();
            var totalParentCount = root.DepthFirstTraverse().Count() - totalLeafCount;
            int groupCountOffset = 0;
            foreach (var group in root.GetReverseDepthGroups())
            {
                Parallel.ForEach(group, (node, pls, index) =>
                {
                    // Check to see all children have meshes, otherwise defere processing
                    if (!node.AllChildrenHaveMeshes())
                    {
                        return;
                    }
                    logger.Info(string.Format("Parent: {0} ({1}/{2})", node.Name, index + groupCountOffset, totalParentCount));
                    node.BuildGeometryFromChildren(root, options.TargetFacesPerTile, options.MaxResolutionPerTile, options.SkirtAxis);
                    node.SaveMesh(options.OutputDirectory, meshExtension: options.MeshExtension, imageExtension: options.ImageExtension);
                    logger.Info(node.GetComponent<MeshImagePair>().Mesh.Faces.Count);
                });
                groupCountOffset += group.Count();
            }
        }
    }
}
