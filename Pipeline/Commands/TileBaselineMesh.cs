using CommandLine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Geometry;
using System.Collections.Concurrent;
using System.IO;
using OPS.Imaging;
using OPS.Util;
using log4net;
using Newtonsoft.Json;
using Microsoft.Xna.Framework;
using OPS.MathExtensions;

namespace OPS.Pipeline
{
    [Verb("tilebaselinemesh", HelpText = "Converts a basline mesh 3d tiles")]
    public class TileBaselineMeshOptions
    {
        [Value(0, Required = true, HelpText = "Filename of baseline mesh")]
        public string InputMesh { get; set; }

        [Value(1, Required = true, HelpText = "Output directory where output tiles will be saved")]
        public string OutputDir { get; set; }
                
        [Option(HelpText = "Input image to use for this mesh")]
        public string InputImage { get; set; }

        [Option(HelpText = "Reachability image to use when generating alpha")]
        public string ReachImage { get; set; }

        [Option(HelpText = "Convert from local level to unity frame")]
        public bool UseUnityFrame { get; set; }
    }
     
    /// <summary>
    /// Command for converting a baseline mesh into 3D tiles
    /// </summary>           
    public class TileBaselineMesh
    {
        const int FACE_LIMIT_PER_TILE = 2000;
        const int MIN_TILE_RES = 32;
        const int MAX_TILE_RES = 256;
        const string MESH_EXT = "ply";
        const string IMAGE_EXT = "png";

        private static readonly ILog logger = LogManager.GetLogger(typeof(TileBaselineMesh));
        TileBaselineMeshOptions options;

        public TileBaselineMesh(TileBaselineMeshOptions opts)
        {
            this.options = opts;
        }

        public string NodeToId(SceneNode node)
        {
            string id = "";
            while(node.Transform.Parent != null)
            {
                int i = node.Parent.Children.ToList().IndexOf(node);
                id = i + id;
                node = node.Transform.Parent.Node;
            }
            return "0" + id;
        }

        string NodeToUrl(SceneNode node)
        {
            return NodeToId(node) + "." + MESH_EXT;
        }

        string NodeToMeshFilename(SceneNode node)
        {
            return Path.Combine(options.OutputDir, NodeToId(node) + "." + MESH_EXT);
        }

        string NodeToImageFilename(SceneNode node)
        {
            return Path.ChangeExtension(NodeToMeshFilename(node), "." + IMAGE_EXT);
        }

        public int Run()
        {
            // Make sure we have an output directory before we do a lot of work
            PathHelper.EnsureExists(options.OutputDir);

            bool hasPDSInputImage = options.InputImage != null && Path.GetExtension(options.InputImage).ToUpper() == ".IMG";
            if (!hasPDSInputImage && options.UseUnityFrame)
            {
                logger.Error("Cannot convert to unity frame without an IMG file");
                return -1;
            }

            // Import the image if there is one
            Vector3 originOffset = Vector3.Zero;
            IMeshAtlaser atlaser = null;
            IImageProducer imageProducer = null;
            if (this.options.InputImage != null)
            {
                Image inputImage = Image.Load(this.options.InputImage);
                if(options.ReachImage != null)
                {
                    logger.Info("Loading reachability");
                    inputImage = RoverReachability.GenerateAlphaFromArmImage(inputImage, Image.Load(options.ReachImage));
                }
                imageProducer = new BaselineImageProducer(inputImage, MIN_TILE_RES, MAX_TILE_RES * MAX_TILE_RES);
                // If this is a PDS image create an atlaser             
                if (hasPDSInputImage)
                {
                    var parser = new PDSParser((PDSMetadata)inputImage.Metadata);
                    originOffset = parser.OriginOffset;                   
                    Matrix localLevelToRover = RoverCoordinateSystem.LocalLevelToRover(parser.RoverOriginRotation);
                    Matrix meshToCameraModel = localLevelToRover;
                    if(options.UseUnityFrame)
                    {
                        meshToCameraModel = Matrix.Multiply(RoverCoordinateSystem.UnityToLocalLevel, localLevelToRover);
                    }
                    atlaser = new BaselineAtlaser(meshToCameraModel, inputImage);
                }
            }
            // Import the mesh
            logger.Info("Loading " + this.options.InputMesh);
            List<Mesh> lods = new List<Mesh>();
            lods.Add(Mesh.Load(this.options.InputMesh));
            foreach(var m in lods)
            {
                // Convert mesh to local level frame
                RoverCoordinateSystem.SiteToLocalLevelMesh(m, originOffset);
                // Clean the mesh
                m.Clean();
                if (this.options.UseUnityFrame)
                {
                    RoverCoordinateSystem.LocalLevelToUnityMesh(m);
                }
            }
            // Don't atlas the tiles if the original mesh didn't doesn't have uvs
            if(!lods[0].HasUVs)
            {
                atlaser = null;
            }
            // Compute normals for any lods that don't have them
            if(lods.Any(lod => !lod.HasNormals))
            {
                logger.Info("Computing normals");
            }
            for (int i = 0; i < lods.Count; i++)
            {               
                if (!lods[i].HasNormals)                    
                {
                    lods[i] = MeshLab.ComputeNormals(lods[i]);
                }
            }
            // Create mesh operator to accelerate clipping
            logger.Info("Creating mesh operator");
            MeshOperator meshOperator = new MeshOperator(lods[0]);
            // Sanity check that we did all of our coordinate math correctly and that the atlaser is giving uvs that match 
            // those on the base mesh 
            if (lods[0].HasUVs && atlaser != null)
            {
                logger.Info("Atlas Accuracy Test");
                var atlased = atlaser.GenerateAtlas(lods[0]);
                for (int i = 0; i < atlased.Vertices.Count; i++)
                {
                    if (!Vector2.AlmostEqual(atlased.Vertices[i].UV, lods[0].Vertices[i].UV, 0.001))
                    {
                        throw new Exception("Error with atlas accuracy");
                    }
                }
            }
            // Configure tiling scheme
            // In local level up down is in Z-axis but in Unity it is Y-axis
            QuadTreeTilingScheme.SplitDirection splitDirection = QuadTreeTilingScheme.SplitDirection.Z;
            if(options.UseUnityFrame)
            {
                splitDirection = QuadTreeTilingScheme.SplitDirection.Y;
            }
            QuadTreeTilingScheme tilingScheme = new QuadTreeTilingScheme(splitDirection);
            // Setup tiler to split tiles based on number of faces
            MeshTiler tiler = new MeshTiler(meshOperator, meshOperator.Bounds, tilingScheme, new FaceLimitSplitCriteria(FACE_LIMIT_PER_TILE));
            IContentProducer leafProducer = new DefaultLeafTileContentProducer(imageProducer);
            // Chose a parent tile producer depending on if the original file had multiple LODs 
            IContentProducer parentProducer;
            if (lods.Count == 1)
            {
                parentProducer = new DefaultParentTileContentProducer(FACE_LIMIT_PER_TILE, tilingScheme, atlaser, imageProducer);
            }
            else
            {
                List<MeshOperator> mos = new List<MeshOperator>();
                // Skip highest LOD because it will be used for leaf tiles
                for(int i = 1; i < lods.Count; i++) {
                    mos.Add(new MeshOperator(lods[i]));
                }
                parentProducer = new PrecomputedLODParentTileContentProducer(mos, FACE_LIMIT_PER_TILE);
            }
            logger.Info("Building mesh tiles");
            tiler.Run(LoadNode, SaveNode, leafProducer, parentProducer);
           
            logger.Info("Creating tileset");
            Tile3DBuilder builder = new Tile3DBuilder(tiler.Root);
            logger.Info("Calculating geometric error between tiles");
            builder.CalculateGeometricError();
            builder.BuildTileset(NodeToUrl);
            string s = JsonConvert.SerializeObject(builder.Tileset, Formatting.Indented);
            File.WriteAllText(Path.Combine(options.OutputDir, "tileset.json"), s);

            logger.Info("Done");
            return 0;
        }

        /// <summary>
        /// Load a nodes mesh and image from disk
        /// </summary>
        /// <param name="node"></param>
        void LoadNode(SceneNode node)
        {
            if (File.Exists(NodeToMeshFilename(node)))
            { 
                string imageName = NodeToImageFilename(node);
                Image img = null;
                if(File.Exists(imageName))
                {
                    img = Image.Load(imageName);
                }               
              
                MeshImagePair pair = node.AddComponent<MeshImagePair>();
                pair.Mesh = Mesh.Load(NodeToMeshFilename(node));
                pair.Image = img;
            }
        }

        /// <summary>
        /// Save a nodes mesh and image to disk
        /// </summary>
        /// <param name="node"></param>
        void SaveNode(SceneNode node)
        {
            MeshImagePair pair = node.GetComponent<MeshImagePair>();
            if (pair != null && pair.Mesh != null)
            {
                string imageName = null;
                if (pair.Image != null)
                {
                    imageName = NodeToImageFilename(node);
                    pair.Image.Save<byte>(imageName);
                }
                PLYSerializer.Write(pair.Mesh, NodeToMeshFilename(node), new PLYCompactFileWriter(), imageName);
            }
        }
    }


    [Verb("tilebaselinemeshes", HelpText = "Converts a directory of basline meshs into 3d tiles")]
    public class TileBaselineMeshesOptions
    {
        [Value(0, Required = true, HelpText = "Input Directory")]
        public string InputDir { get; set; }

        [Value(1, Required = true, HelpText = "Output Directory")]
        public string OutputDir { get; set; }

        [Option(HelpText = "Convert from local level to unity frame")]
        public bool UseUnityFrame { get; set; }
    }

    /// <summary>
    /// Command for batch processing baseline meshes to tiles
    /// </summary>
    public class TileBaselineMeshes
    {
        TileBaselineMeshesOptions options;
        private static readonly ILog logger = LogManager.GetLogger(typeof(TileBaselineMeshes));

        public TileBaselineMeshes(TileBaselineMeshesOptions options)
        {
            this.options = options;
        }

        public int Run()
        {
            // Search for all RASL iv files in the direcotry
            foreach (string meshFile in Directory.EnumerateFiles(options.InputDir, "*RASL*.iv"))
            {
                TileBaselineMeshOptions opts = new TileBaselineMeshOptions();
                opts.InputMesh = meshFile;
                opts.OutputDir = Path.Combine(options.OutputDir, Path.GetFileNameWithoutExtension(meshFile));
                // Skip if we have already processed
                if (File.Exists(Path.Combine(opts.OutputDir, "tileset.json")))
                {
                    logger.Debug(meshFile + " skip");
                    continue;
                }
                logger.Debug(meshFile + " process");
                // Use latest image version available
                string imageFile = FindHighestVersionImage(meshFile.Replace(".iv", ".IMG"));
                if (File.Exists(imageFile))
                {
                    logger.Debug(imageFile + " found image");
                    opts.InputImage = imageFile;
                }
                // Use latest reachability version available
                string reachFile = FindHighestVersionImage(meshFile.Replace("RASL", "ARML").Replace(".iv", ".IMG"));
                if (File.Exists(reachFile))
                {
                    logger.Debug(reachFile + " found reach");
                    opts.ReachImage = reachFile;
                }
                opts.UseUnityFrame = options.UseUnityFrame;
                int r = new TileBaselineMesh(opts).Run();
                if (r != 0)
                {
                    return r;
                }
            }
            return 0;
        }

        /// <summary>
        /// Returns the most recent file version that can be found
        /// Only works for versions 0-9
        /// </summary>
        /// <param name="filename"></param>
        /// <returns></returns>
        public static string FindHighestVersionImage(string filename)
        {
            string template = filename.Replace(".IMG", "");
            template = template.Substring(0, template.Length - 1);
            string r = null;
            for (int version = 0; version < 9; version++)
            {
                string curname = template + version + ".IMG";
                if (File.Exists(curname))
                {
                    r = curname;
                }
            }
            return r;
        }
    }
}
