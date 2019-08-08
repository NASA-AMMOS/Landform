using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using CommandLine;
using log4net;
using OPS.Util;
using OPS.Imaging;
using OPS.Geometry;
using OPS.Pipeline.TilingServer;

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

        [Option(Required = false, Default = 256, HelpText = "Maxium texture size for a tile")]
        public int MaxTextureSize { get; set; }

        [Option(Required = false, Default = 2000, HelpText = "Number of allowed faces per tile")]
        public int MaxFacesPerTile { get; set; }

        [Option(Required = false, Default = "jpg", HelpText = "Export format for textures (examples: jpg or png (dds or crn)")]
        public string ImageFormat { get; set; }

        [Option(Required = false, Default = SkirtMode.Y, HelpText = "Axis to use as up in quad tree tiling")]
        public SkirtMode SkirtAxis { get; set; }
    }

    /// <summary>
    /// Convert a legacy OnSight scene to 3D Tiles format
    /// </summary>
    public class LegacyToTile3D
    {

        class GeometricErrorPlaceholder : NodeComponent
        {
            double Error;

            public GeometricErrorPlaceholder()
            {

            }

            public GeometricErrorPlaceholder(double error)
            {
                this.Error = error;
            }

        }

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


            logger.Info("Building sky dataset.");            
            CoreLimitedParallel.ForEach(scene.SkyRoot.DepthFirstTraverse(), node =>
            {
                if (node.Name.ToLower() != "sky")
                {
                    node.Name = "s" + node.Name;
                }
                if (node.IsLeaf)
                {
                    var m = node.GetComponent<MeshImagePair>().Mesh;
                    // Create normals that point toward the origin
                    m.HasNormals = true;
                    for(int i = 0; i < m.Vertices.Count; i++)
                    {
                        m.Vertices[i].Normal = -Vector3.Normalize(m.Vertices[i].Position);
                    }
                    SaveNode(node, "png");
                }
                node.AddComponent(new NodeGeometricError(node.GetComponent<NodeBounds>().Bounds.MaxDimension()));
            });

            Tile3DBuilder skyBuilder = new Tile3DBuilder(scene.SkyRoot);
            skyBuilder.BuildTileset(NodeToUrl, false);
            string jsonDataSky = JsonConvert.SerializeObject(skyBuilder.Tileset, Formatting.None);
            File.WriteAllText(Path.Combine(options.OutputDirectory, "tilesetSky.json"), jsonDataSky);


            var terrainRoot = scene.TerrainRoot;
            logger.Info("Removing skirts and non-leaf data.");
            CoreLimitedParallel.ForEach(terrainRoot.DepthFirstTraverse(), node =>
            {
                node.RemoveComponent<NodeBounds>();
                if (!node.IsLeaf)
                {
                    node.RemoveComponent<MeshImagePair>();                    
                }
                else
                {
                    var pair = node.GetComponent<MeshImagePair>();
                    pair.Mesh.Clean();
                    pair.Mesh.RemoveSkirt(options.SkirtAxis);
                    if (!pair.Mesh.HasNormals)
                    {                        
                        pair.Mesh.GenerateVertexNormals();
                    }
                }
            });
            logger.Info("Subdivide large leaves");
            bool anyOversized = true;
            while (anyOversized)
            {
                anyOversized = false;
                CoreLimitedParallel.ForEach(terrainRoot.Leaves().ToList(), node =>
                {
                    var mip = node.GetComponent<MeshImagePair>();
                    if (mip.Image.Width > options.MaxTextureSize || mip.Image.Height > options.MaxTextureSize || mip.Mesh.Faces.Count > options.MaxFacesPerTile)
                    {
                        logger.Info("Subdividing:" + node.Name);
                        anyOversized = true;
                        var meshCopy = new Mesh(mip.Mesh);
                        meshCopy.ClearUVs();
                        meshCopy.ClearNormals();
                        meshCopy.Clean();
                        MeshOperator mo = new MeshOperator(meshCopy);
                        var tilingScheme = new QuadTreeTilingScheme(QuadTreeAxis.Y);
                        var boxes = tilingScheme.Split(mo, mo.Bounds);
                        int i = 0;
                        foreach (var box in boxes)
                        {
                            var m = mo.Clip(box);
                            m = UVAtlas.Atlas(m);
                            var img = TextureBaker.BakeTexture(new MeshImagePair[] { mip }, m, mip.Image.Width / 2, mip.Image.Height / 2);
                            var child = new SceneNode(node.Name + i);
                            child.AddComponent(new MeshImagePair(m, img));
                            child.Transform.Parent = node.Transform;
                            i++;
                        }
                        node.RemoveComponent<MeshImagePair>();
                        node.RemoveComponent<NodeBounds>();
                    }
                });
            }
            logger.Info("Write leaves");
            CoreLimitedParallel.ForEach(terrainRoot.Leaves(), node =>
            {
                var mesh = node.GetComponent<MeshImagePair>().Mesh;
                mesh.GenerateVertexNormals();
                if (options.SkirtAxis != SkirtMode.None)
                {
                    mesh.AddSkirt(options.SkirtAxis);
                }
                // Important, bounds include skirts
                node.AddComponent(new NodeBounds(mesh.Bounds()));
                node.AddComponent(new NodeGeometricError(0));
                SaveNode(node, options.ImageFormat);
            });

            var depthGroups = terrainRoot.DepthFirstTraverse()
                .Where(n => !n.IsLeaf)
                .GroupBy(n => n.Transform.Depth())
                .OrderBy(g => -g.Key);

            logger.Info("Generate bounds");
            foreach (var group in depthGroups)
            {
                CoreLimitedParallel.ForEach(group, node =>
                {
                    var childBounds = node.Children.Select(c => c.GetComponent<NodeBounds>().Bounds).ToArray();
                    var bounds = BoundingBoxExtensions.Union(childBounds);
                    node.AddComponent(new NodeBounds(bounds));
                });
            }
            
            logger.Info("Generate parents");
            int totalParents = terrainRoot.DepthFirstTraverse().Where(n => !n.IsLeaf).Count();
            int parentsCompleted = 0;
            foreach (var group in depthGroups)
            {
                CoreLimitedParallel.ForEach(group, node => {

                    Interlocked.Increment(ref parentsCompleted);
                    int percentDone = (int)((parentsCompleted / (float)totalParents) * 100);
                    logger.Info("Creating parent data:" + node.Name + " (" + percentDone + "%)");
                    // Check to see all children have meshes, otherwise defer processing
                    bool canMakeMesh = node.Children.All(n => n.HasComponent<MeshImagePair>());
                    if (!canMakeMesh)
                    {
                        return;
                    }
                    node.BuildGeometryFromChildren(terrainRoot, MeshReconMethod.Poisson,
                                                   options.MaxFacesPerTile, options.MaxTextureSize, options.SkirtAxis);
                    if (options.SkirtAxis != SkirtMode.None)
                    {
                        var m = node.GetComponent<MeshImagePair>().Mesh;
                        m.AddSkirt(options.SkirtAxis);
                        var nb = node.GetComponent<NodeBounds>();
                        nb.Bounds = BoundingBoxExtensions.Union(nb.Bounds, m.Bounds());
                    }
                    SaveNode(node, options.ImageFormat);
                });
            }
            Tile3DBuilder builder = new Tile3DBuilder(terrainRoot);
            builder.BuildTileset(NodeToUrl, false);
            string jsonData = JsonConvert.SerializeObject(builder.Tileset, Formatting.None);
            File.WriteAllText(Path.Combine(options.OutputDirectory, "tileset.json"), jsonData);
            return 0;
        }

        void SaveNode(SceneNode node, string imageFormat)
        {
            node.SaveMesh(options.OutputDirectory, "b3dm", imageFormat);
        }

        string NodeToUrl(SceneNode node)
        {
            return node.Name + ".b3dm";
        }
    }
}
