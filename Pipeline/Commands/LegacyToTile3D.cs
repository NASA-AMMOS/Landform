using CommandLine;
using log4net;
using OPS.Geometry;
using OPS.Util;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using System.IO;
using OPS.Imaging;

namespace OPS.Pipeline
{

    [Verb("legacytotile3d", HelpText = "Convert a legacy OnSight scene to 3D Tiles")]
    public class LegacyToTile3DOptions
    {
        [Value(0, Required = true, HelpText = "Directory containing legacy tiles")]
        public string InputDirectory { get; set; }

        [Value(1, Required = true, HelpText = "Directory to write new tiles to")]
        public string OutputDirectory { get; set; }

        [Option(Required = false, Default = 4096, HelpText = "Total extent of legacy tiles")]
        public int InputExtent { get; set; }

        [Option(Required = false, Default = 2048, HelpText = "Maxium texture size for a tile")]
        public int MaxTextureSize { get; set; }

        [Option(Required = false, Default = 2000, HelpText = "Number of allowed faces per tile")]
        public int FacesPerTile { get; set; }

        [Option(Required = false, Default = "jpg", HelpText = "Export format for textures (examples: jpg or png")]
        public string ImageFormat { get; set; }
    }

    /// <summary>
    /// Convert a legacy OnSight scene to 3D Tiles format
    /// </summary>
    public class LegacyToTile3D
    {

        private static readonly ILog logger = LogManager.GetLogger(typeof(LegacyToTile3D));

        private LegacyToTile3DOptions options;

        public LegacyToTile3D(LegacyToTile3DOptions options)
        {
            this.options = options;
        }

        public int Run()
        {
            PathHelper.EnsureExists(options.OutputDirectory);

            logger.Info("Loading legacy scene");
            LegacyScene scene = new LegacyScene(options.InputDirectory, options.InputExtent);

            var terrainRoot = scene.TerrainRoot;
            logger.Info("Removing skirts and non-leaf data.");
            Parallel.ForEach(terrainRoot.DepthFirstTraverse(), node =>
            {
                if (node.IsLeaf)
                {
                    var pair = node.GetComponent<MeshImagePair>();

                    string cacheMeshName = Path.Combine(options.OutputDirectory, node.Name + ".ply");
                    if (File.Exists(cacheMeshName))
                    {
                        pair.Mesh = Mesh.Load(cacheMeshName);
                        pair.Mesh.RemoveSkirt(SkirtAxis.Y);
                    }
                    else
                    {
                        pair.Mesh.RemoveSkirt(SkirtAxis.Y);
                        if (!pair.Mesh.HasNormals)
                        {
                            pair.Mesh.Clean();
                            pair.Mesh.GenerateVertexNormals();
                        }
                        SaveNode(node);
                    }
                }
                else if (node.HasComponent<MeshImagePair>())
                {
                    node.RemoveComponent<MeshImagePair>();
                }
            });
            logger.Info("Process nodes");
            // Generate a list of nodes that are parents of leaves.  Hashset will remove duplicates
            var nodesToProcess = new HashSet<SceneNode>(terrainRoot.Leaves().Select(n => n.Parent));
            while (nodesToProcess.Count != 0)
            {
                ConcurrentQueue<SceneNode> nextNodesToProcess = new ConcurrentQueue<SceneNode>();
                Parallel.ForEach(nodesToProcess, node =>
                {
                    if (node == null)
                    {
                        return;
                    }
                    // Skip this node if it already has a mesh image pair 
                    // This shouldn't usually happen unless someone deleted some stuff from the cache
                    if (node.HasComponent<MeshImagePair>())
                    {
                        nextNodesToProcess.Enqueue(node.Parent);
                        return;
                    }
                    // try load from cache
                    string cacheMeshName = Path.Combine(options.OutputDirectory, node.Name + ".ply");
                    if (File.Exists(cacheMeshName))
                    {
                        Mesh mesh = Mesh.Load(cacheMeshName);
                        mesh.RemoveSkirt(SkirtAxis.Y);
                        Image image = Image.Load(cacheMeshName.Replace("ply", options.ImageFormat));
                        node.AddComponent<MeshImagePair>(new MeshImagePair(mesh, image));
                        nextNodesToProcess.Enqueue(node.Parent);
                        return;
                    }
                    // Check to see all children have meshes, otherwise defere processing
                    bool canMakeMesh = node.Children.All(n => n.HasComponent<MeshImagePair>());
                    if (!canMakeMesh)
                    {
                        nextNodesToProcess.Enqueue(node);
                        return;
                    }
                    logger.Info("Creating parent data:" + node.Name);
                    var pairs = node.Children.Select(n => n.GetComponent<MeshImagePair>());
                    Mesh m = Mesh.Merge(pairs.Select(p => p.Mesh).ToArray());
                    var originalBounds = m.Bounds();
                    m.NormalizeNormals();
                    int targetFaces = m.Faces.Count() / 3;  // could do 4 but lets try 3 for some extra around the edges
                    targetFaces = Math.Min(targetFaces, options.FacesPerTile);
                    m = LegacyToWebVR.ResampleDecimation(m, targetFaces, clippingBounds: m.Bounds(), cornerDirection: Vector3.Up);
                    m.Clean();
                    // We want a 2x reduction in both size dimensions (4x reduction in area)
                    int size = LegacyToWebVR.ComputeParentTileResolution(pairs, m.Bounds()) / 2;
                    size = Math.Min(size, options.MaxTextureSize);
                    m = UVAtlas.Atlas(m, size, size);
                    var img = TextureBaker.BakeTexture(pairs.ToArray(), m, size, size);
                    // We need to combine bounds here because decimated bounds may be smaller than the child bounds
                    node.Bounds = BoundingBox.CreateMerged(m.Bounds(), originalBounds);// m.Bounds();
                    node.AddComponent(new MeshImagePair(m, img));
                    SaveNode(node);
                    nextNodesToProcess.Enqueue(node.Parent);
                });
                nodesToProcess = new HashSet<SceneNode>(nextNodesToProcess);;
            }
            Tile3DBuilder builder = new Tile3DBuilder(terrainRoot);
            builder.BuildTileset(NodeToUrl, false);
            logger.Info("Calculating geometric error between tiles");
            builder.CalculateGeometricError();
            string s = JsonConvert.SerializeObject(builder.Tileset, Formatting.Indented);
            File.WriteAllText(Path.Combine(options.OutputDirectory, "tileset.json"), s);
            return 0;
        }

        void SaveNode(SceneNode node)
        {
            var pair = node.GetComponent<MeshImagePair>();
            Mesh m = new Mesh(pair.Mesh);
            m.GenerateVertexNormals();
            m.AddSkirt(SkirtAxis.Y);
            TemporaryFile.GetAndDelete("." + options.ImageFormat, f =>
            {
                pair.Image.Save<byte>(f);
                m.Save(Path.Combine(options.OutputDirectory, NodeToUrl(node)), f);
                m.Save(Path.Combine(options.OutputDirectory, node.Name + ".glb"), f);
            });

            string imgName = Path.Combine(options.OutputDirectory, node.Name + "." + options.ImageFormat);
            pair.Image.Save<byte>(imgName);
            m.Save(Path.Combine(options.OutputDirectory, node.Name + ".ply"), imgName);
        }

        string NodeToUrl(SceneNode node)
        {
            return node.Name + ".b3dm";
        }
    }
}
