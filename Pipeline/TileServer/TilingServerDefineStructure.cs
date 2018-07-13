using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Geometry;
using OPS.Imaging;
using log4net;
using CommandLine;
using OPS.Pipeline.TileServer;

namespace OPS.Pipeline.TileServer
{

    [Verb("tilingserverdefinestructure", HelpText = "")]
    public class TilingServerDefineStructureOptions
    {
        [Option(Required = false, Default = 2000, HelpText = "Target maximum faces per tile")]
        public int TargetFacesPerTile { get; set; }

        [Option(Required = false, Default = 256, HelpText = "Maximum image resolution per tile")]
        public int MaxResolutionPerTile { get; set; }

        [Option(Required = false, Default = TilingScheme.Oct, HelpText = "Tiling scheme")]
        public TilingScheme TilingScheme { get; set; }

        [Option(Required = false, Default = SkirtMode.None, HelpText = "Axis to use as up in quad tree tiling")]
        public SkirtMode SplitAxis { get; set; }

    }

    public class TilingServerDefineStructure
    {
        static ILog logger = LogManager.GetLogger(typeof(TilingServerDefineStructure));

        TilingServerDefineStructureOptions options;
        public TilingServerDefineStructure(TilingServerDefineStructureOptions options)
        {
            this.options = options;
        }

        public int Run()
        {
            // TODO: refactor reused code between this and TileLocalMesh
            var tilingInput = new TileLocalMesh.TilingInput();
            foreach(var input in PretendTilingServerDatabase.Instance.InputTable)
            {
                tilingInput.AddDataset(new TileLocalMesh.TilingInputDataset(input.MeshFilename, input.ImageFilename));
            }
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
                scheme = new QuadTreeTilingScheme(options.SplitAxis);
            }
            // TODO: Add image size criteria
            ITileSplitCriteria splitCriteria = new FaceLimitSplitCriteria(options.TargetFacesPerTile);
            SceneNode root = TileLocalMesh.BuildBoundsTree(tilingInput, scheme, splitCriteria);
            foreach(var node in root.DepthFirstTraverse())
            {
                var r = new NodeRecord()
                {
                    Id = node.Name,
                    ChildIds = node.Children.Select(c => c.Name).ToArray(),
                    Bounds = node.GetComponent<NodeBounds>().Bounds,
                    ParentId = (node.Parent == null) ? null : node.Parent.Name
                };
                PretendTilingServerDatabase.Instance.NodeTable.Add(r);
            }
            return 0;
        }
    }
}
