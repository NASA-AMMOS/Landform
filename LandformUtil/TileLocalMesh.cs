using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using log4net;
using CommandLine;
using Newtonsoft.Json;
using OPS.Util;
using OPS.Geometry;
using OPS.Pipeline;
using OPS.Imaging;
using OPS.Pipeline.TilingServer;

namespace OPS.LandformUtil
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

    }

    public class TileLocalMesh
    {
        private static readonly ILog logger = LogManager.GetLogger(typeof(TileLocalMesh));

        private TileLocalMeshOptions options;

        private bool SkirtsEnabled { get { return options.SkirtAxis != SkirtMode.None; } }

        public TileLocalMesh(TileLocalMeshOptions opts)
        {
            this.options = opts;
        }

        public int Run()
        {
            PathHelper.EnsureExists(options.OutputDirectory);
            logger.Info("Loading Input");
            MultiMeshClipper multiClipper = new MultiMeshClipper();
            multiClipper.AddInput(new MultiMeshClipperInput(options.InputMesh, options.InputTexture));
            logger.Info("Init Texture Baker");
            multiClipper.InitTextureBaker();
            ITilingScheme scheme;
            if (options.TilingScheme == TilingScheme.Oct)
            {
                scheme = new OctreeTilingScheme();
            }
            else if (options.TilingScheme == TilingScheme.Bin)
            {
                scheme = new BinaryTreeTilingScheme();
            }
            else if(options.TilingScheme == TilingScheme.QuadX || options.TilingScheme == TilingScheme.QuadY || options.TilingScheme == TilingScheme.QuadZ)
            {
                scheme = new QuadTreeTilingScheme(options.TilingScheme);
            }
            else
            {
                throw new Exception("Tiling scheme not yet supported");   
            }

            logger.Info("Computing tree bounds");
            SceneNode root =
                DefineTiles.
                BuildBoundsTree(multiClipper, scheme,
                                new ITileSplitCriteria[] { new FaceSplitCriteria(options.TargetFacesPerTile) });
            logger.Info("Process leaf nodes");
            ProcessLeafNodes(multiClipper, root);
            logger.Info("Generate parents");
            BuildParents(root, options.TargetFacesPerTile, options.MaxResolutionPerTile, SkirtsEnabled,
                         options.SkirtAxis, options.OutputDirectory, options.MeshExtension, options.ImageExtension);
            logger.Info("Generate tileset");
            Tile3DBuilder builder = new Tile3DBuilder(root);
            builder.BuildTileset(NodeToUrl, false);
            string jsonData = JsonConvert.SerializeObject(builder.Tileset, Formatting.None);
            File.WriteAllText(Path.Combine(options.OutputDirectory, "tileset.json"), jsonData);
            return 0;
        }

        private string NodeToUrl(SceneNode node)
        {
            return node.Name + ".b3dm";
        }

        private void ProcessLeafNodes(MultiMeshClipper multiMeshClipper, SceneNode root)
        {
            var totalLeafCount = root.Leaves().Count();
            CoreLimitedParallel.ForEach(root.Leaves(), (node, pls, index) =>
            {
                logger.InfoFormat("Leaf: {0} ({1}/{2})", node.Name, index, totalLeafCount);
                Mesh m = multiMeshClipper.Clip(node.GetComponent<NodeBounds>().Bounds);
                Image img = null;
                if(options.InputTexture != null)
                {
                    var pair = multiMeshClipper.BakeTexture(m, options.MaxResolutionPerTile);
                    if(pair.Mesh != null && pair.Image != null)
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
                node.SaveMesh(options.OutputDirectory,
                              meshExtension:  options.MeshExtension, imageExtension: options.ImageExtension);
            });
        }

        private void BuildParents(SceneNode root,  int targetFacesPerTile, int maxResolutionPerTile,
                                  bool skirtsEnabled, SkirtMode skirtAxis,
                                  string outputDirectory, string meshExtension, string imageExtension)
        {
            var totalLeafCount = root.Leaves().Count();
            var totalParentCount = root.DepthFirstTraverse().Count() - totalLeafCount;
            int groupCountOffset = 0;
            foreach (var group in root.GetReverseDepthGroups())
            {
                CoreLimitedParallel.ForEach(group, (node, pls, index) =>
                {
                    // Check to see all children have meshes, otherwise defer processing
                    if (!node.AllChildrenHaveMeshes())
                    {
                        return;
                    }
                    logger.InfoFormat("Parent: {0} ({1}/{2})", node.Name, index + groupCountOffset, totalParentCount);
                    if(false == node.BuildGeometryFromChildren(root, MeshReconMethod.Poisson,
                                                   targetFacesPerTile, maxResolutionPerTile,
                                                   skirtAxis))
                    {
                        logger.InfoFormat("Failed to build parent: {0} ({1}/{2})", node.Name, index + groupCountOffset, totalParentCount);
                        return;
                    }
                    
                    if (skirtsEnabled)
                    {
                        var m = node.GetComponent<MeshImagePair>().Mesh;
                        m.AddSkirt(skirtAxis);
                        var nb = node.GetComponent<NodeBounds>();
                        nb.Bounds = BoundingBoxExtensions.Union(nb.Bounds, m.Bounds());
                    }
                    node.SaveMesh(outputDirectory,
                                  meshExtension: meshExtension, imageExtension: imageExtension);
                    logger.Info(node.GetComponent<MeshImagePair>().Mesh.Faces.Count);
                });
                groupCountOffset += group.Count();
            }
        }
    }
}
