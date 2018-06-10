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

        [Option(Required = false, Default = 256, HelpText = "Maxium texture size for a tile")]
        public int MaxTextureSize { get; set; }

        [Option(Required = false, Default = 2000, HelpText = "Number of allowed faces per tile")]
        public int MaxFacesPerTile { get; set; }

        [Option(Required = false, Default = "jpg", HelpText = "Export format for textures (examples: jpg or png (dds or crn)")]
        public string ImageFormat { get; set; }
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
            Parallel.ForEach(scene.SkyRoot.DepthFirstTraverse(), node =>
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
            Parallel.ForEach(terrainRoot.DepthFirstTraverse(), node =>
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
                    pair.Mesh.RemoveSkirt(SkirtAxis.Y);
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
                Parallel.ForEach(terrainRoot.Leaves().ToList(), node =>
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
                        var tilingScheme = new QuadTreeTilingScheme(SkirtAxis.Y);
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
            Parallel.ForEach(terrainRoot.Leaves(), node =>
            {                
                GenerateNormalsAndSkirt(node.GetComponent<MeshImagePair>().Mesh);
                // Important, bounds include skirts
                node.AddComponent(new NodeBounds(node.GetComponent<MeshImagePair>().Mesh.Bounds()));
                node.AddComponent(new NodeGeometricError(0));
                SaveNode(node, options.ImageFormat);
            });

            var depthGroups = terrainRoot.DepthFirstTraverse().Where(n => !n.IsLeaf).GroupBy(n => n.Transform.Depth()).OrderBy(g => -g.Key);

            // TODO: this should be removable in the future with the new BuildGeometryFromChildren method
            logger.Info("Generate bounds");
            foreach (var group in depthGroups)
            {
                Parallel.ForEach(group, node =>
                {
                    var childBounds = node.Children.Select(c => c.GetComponent<NodeBounds>().Bounds).ToArray();
                    var bounds = BoundingBoxExtensions.Union(childBounds);
                    node.AddComponent(new NodeBounds(bounds));
                });
            }
            
            logger.Info("Generate parents");
            foreach (var group in depthGroups)
            {
                Parallel.ForEach(group, node =>
                {
                    // Check to see all children have meshes, otherwise defere processing
                    bool canMakeMesh = node.Children.All(n => n.HasComponent<MeshImagePair>());
                    if (!canMakeMesh)
                    {
                        return;
                    }
                    BuildParent(terrainRoot, node);
                    SaveNode(node, options.ImageFormat);
                });
            }

            Tile3DBuilder builder = new Tile3DBuilder(terrainRoot);
            builder.BuildTileset(NodeToUrl, false);
            string jsonData = JsonConvert.SerializeObject(builder.Tileset, Formatting.None);
            File.WriteAllText(Path.Combine(options.OutputDirectory, "tileset.json"), jsonData);
            return 0;
        }

        // TODO: replace with version in SceneNodeTilingExtensions
        public List<SceneNode> FindSceneNodes(SceneNode root, int minDepth, BoundingBox box)
        {
            List<SceneNode> result = new List<SceneNode>();
            Stack<SceneNode> stack = new Stack<SceneNode>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                SceneNode node = stack.Pop();
                if (node == null)
                {
                    continue;
                }
                BoundingBox nodeBounds = node.GetComponent<NodeBounds>().Bounds;
                if (!nodeBounds.Intersects(box))
                {
                    continue;
                }
                if (node.IsLeaf || node.Transform.Depth() >= minDepth)
                {
                    result.Add(node);
                    continue;
                }
                else
                {
                    foreach (var child in node.Transform.Children.Select(t => t.Node))
                    {
                        stack.Push(child);
                    }
                }
            }
            return result;
        }

        // TODO replace with version in SceneNodeTilingExtensions
        public void BuildParent(SceneNode root, SceneNode parent)
        {
            logger.Info("Creating parent data:" + parent.Name);
            int childDepth = parent.Children.First().Transform.Depth();
            BoundingBox searchBounds = parent.GetComponent<NodeBounds>().Bounds;
            searchBounds = BoundingBoxExtensions.Scale(searchBounds, 1.6);
            var childNodes = FindSceneNodes(root, childDepth, searchBounds);
            var pairs = childNodes.Select(n => n.GetComponent<MeshImagePair>());
            var childMeshesWithoutSkirts = pairs.Select(p =>
            {
                var tmp = new Mesh(p.Mesh);
                tmp.RemoveSkirt(SkirtAxis.Y);
                return tmp;
            }).ToArray();

            Mesh combinedFull = Mesh.Merge(childMeshesWithoutSkirts);
            combinedFull = Mesh.Clip(combinedFull, searchBounds);
            combinedFull.NormalizeNormals();
            // TODO: handle the fact that we are reconstucting a larger area so we should inflate the number of faces cleverly
            int targetFaces = combinedFull.Faces.Count() / 3;  // could do 4 but lets try 3 for some extra around the edges
            targetFaces = Math.Min(targetFaces, options.MaxFacesPerTile);
            // Minimum bounds is a tight fitting bounding box around the child meshes with skirts
            BoundingBox minimumBounds = parent.GetComponent<NodeBounds>().Bounds;
            Mesh combinedDecimated = combinedFull.ResampleDecimation(targetFaces, clippingBounds:minimumBounds, cornerDirection: Vector3.Up);
            combinedDecimated.Clean();
            if (!parent.HasComponent<NodeGeometricError>())
            {
                Mesh fullClipped = Mesh.Clip(combinedFull, minimumBounds);
                double geometricError = combinedDecimated.HausdorffDistance(fullClipped);
                parent.AddComponent(new NodeGeometricError(geometricError));
            }
            // We want a 2x reduction in both size dimensions (4x reduction in area)
            int size = LegacyToWebVR.ComputeParentTileResolution(pairs, combinedDecimated.Bounds()) / 2;
            size = Math.Min(size, options.MaxTextureSize);
            combinedDecimated = UVAtlas.Atlas(combinedDecimated, size, size);
            var img = TextureBaker.BakeTexture(pairs.ToArray(), combinedDecimated, size, size);
            GenerateNormalsAndSkirt(combinedDecimated);
            // We need to combine bounds here because decimated bounds may be smaller than the child bounds
            var bounds = BoundingBox.CreateMerged(combinedDecimated.Bounds(), minimumBounds);
            parent.GetComponent<NodeBounds>().Bounds = bounds;
            // Add new mesh and image to parent
            parent.AddComponent(new MeshImagePair(combinedDecimated, img));
            // Estimate the size of a pixel for this texture.  If this is greater than the geometric error use it instead
            {
                var ext = minimumBounds.Extent();
                double sizePerPixel = new Vector2(ext.X, ext.Z).Length() / new Vector2(size / 2, size / 2).Length();
                var nge = parent.GetComponent<NodeGeometricError>();
                nge.Error = Math.Max(nge.Error, sizePerPixel);
                // Also ensure geo error is at least as large as children
                foreach(var child in parent.Children)
                {
                    var error = child.GetComponent<NodeGeometricError>().Error;
                    nge.Error = Math.Max(error, nge.Error);
                }
            }
        }

        void GenerateNormalsAndSkirt(Mesh mesh)
        {
            mesh.GenerateVertexNormals();
            mesh.AddSkirt(SkirtAxis.Y);
        }

        // TODO: replace with version in SceneNodeTilingExtensions
        void SaveNode(SceneNode node, string imageFormat)
        {
            var pair = node.GetComponent<MeshImagePair>();
            Mesh m = pair.Mesh;
            TemporaryFile.GetAndDelete("." + imageFormat, f =>
            {
                if (pair.Image != null)
                {
                    pair.Image.Save<byte>(f);
                }
                else
                {
                    f = null;
                }
                m.Save(Path.Combine(options.OutputDirectory, NodeToUrl(node)), f);
                m.Save(Path.Combine(options.OutputDirectory, node.Name + ".glb"), f);
            });
            string imgName = null;
            if (pair.Image != null)
            {
                imgName = Path.Combine(options.OutputDirectory, node.Name + "." + imageFormat);
                pair.Image.Save<byte>(imgName);
            }
            m.Save(Path.Combine(options.OutputDirectory, node.Name + ".ply"), imgName);
        }

        string NodeToUrl(SceneNode node)
        {
            return node.Name + ".b3dm";
        }
    }
}
