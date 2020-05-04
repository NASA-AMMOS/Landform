using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using CommandLine;
using OPS.Geometry;
using OPS.Imaging;
using OPS.Pipeline;
using OPS.Pipeline.AlignmentServer;
using OPS.Pipeline.TilingServer;
using Microsoft.Xna.Framework;
using OPS.Util;
using OPS.Pipeline.Texturing;

/// <summary>
/// creates a tileset containing a fixed sphere mesh to display
/// behind the terrain
/// </summary>
/// 

namespace OPS.Landform
{
    [Verb("build-sky-sphere", HelpText = "build a skysphere tileset from observations")]
    public class BuildSkySphereOptions : BuildTilesetOptions
    {
        [Option(HelpText = "Sky sphere radius (meters)", Default = 200)]
        public double SphereRadiusMeters { get; set; }

        [Option(HelpText = "Sky sphere mesh resolution (degrees)", Default = 5)]
        public double SphereResolutionDegres { get; set; }

        [Option(HelpText = "Sky sphere background color Red (0-255)", Default = 100)]
        public double SkyColorRed { get; set; }

        [Option(HelpText = "Sky sphere background color Green (0-255)", Default = 91)]
        public double SkyColorGreen{ get; set; }

        [Option(HelpText = "Sky sphere background color Red (0-255)", Default = 76)]
        public double SkyColorBlue { get; set; }
    }

    public class BuildSkySphere : BuildTileset
    {
        //public const string TILESET_DIR = "tiling/Tileset/SkySphere";
        private SceneNode tileTree;
        private BuildSkySphereOptions options;
        private List<Backproject.Context> contexts;
        private float[] skyColor = { 0.0f, 0.0f, 0.0f };

        public BuildSkySphere(BuildSkySphereOptions options) : base(options)
        {
            this.options = options;
        }

        public override int Run()
        {
            try
            {
                options.Redo = true;             //triggers delete of previous tiling project results
                options.NoOrbital = true;        //orbital not useful in skysphere
                options.ObsSelectionStrategy = ObsSelectionStrategyName.Exhaustive; //no whole scene mesh used, spatial caching not needed
                options.TextureFarClip = options.SphereRadiusMeters * 2.0;

                if (!ParseArgumentsAndLoadCaches())
                {
                    return 0; //help
                }

                skyColor = new float[] { (float)options.SkyColorRed/255.0f, (float)options.SkyColorGreen / 255.0f, (float)options.SkyColorBlue / 255.0f };

                RunPhase("checking/generating observation image masks", BuildObservationImageMasks);
                RunPhase("load input mesh", () => LoadInputMesh(requireUVs: false));
                RunPhase("build occlusion datastructures", BuildSceneCaster);
                RunPhase("build observation frustum hulls", BuildObsHulls);
                RunPhase("build sphere tile geometry", BuildSphereTiles);
                RunPhase("build sphere tile textures", BuildTileTextures);
                //RunPhase("blend tiles")
                RunPhase("create tiling project", () => CreateTilingProject(TilingScheme.Flat));
                RunPhase("add tile meshes", AddTileMeshes);
                RunPhase("build tiles and define parents", BuildTilesAndDefineParents);
                RunPhase("build parent tiles", BuildParentTiles);
            }
            catch (Exception ex)
            {
                pipeline.LogException(ex);
                return 1;
            }

            StopStopwatch();

            return 0;
        }

        private void BuildSphereTiles()
        {
            double sphereResRad = MathHelper.ToRadians(options.SphereResolutionDegres);
            int rows = (int)(Math.PI / sphereResRad);
            int cols = (int)(2.0 * Math.PI / sphereResRad);

            //generate the verts to be shared by the tiles
            List<Vector3> positions = new List<Vector3>();
            for (int idxRow = 0; idxRow < rows; idxRow++)
            {
                for (int idxCol = 0; idxCol < cols; idxCol++)
                {
                    double el = Math.PI - idxRow * sphereResRad;
                    double az = idxCol * sphereResRad;
                    Vector3 pos = Vector3.Zero;
                    pos.X = options.SphereRadiusMeters * Math.Cos(az) * Math.Sin(el);
                    pos.Y = options.SphereRadiusMeters * Math.Sin(az) * Math.Sin(el);
                    pos.Z = options.SphereRadiusMeters * Math.Cos(el);
                    positions.Add(pos);
                }
            }

            int ToIndex(int row, int col, int numCols)
            {
                return row * cols + col;
            }

            //create mesh tiles
            List<Mesh> tiles = new List<Mesh>();
            for (int idxRow = 1; idxRow < rows; idxRow++)
            {
                int prevRow = idxRow - 1;

                for (int idxCol = 1; idxCol <= cols; idxCol++)
                {
                    int curCol = idxCol;
                    int prevCol = idxCol - 1;

                    if (idxCol == cols)
                    {
                        //wrap
                        curCol = 0;
                        prevCol = cols - 1;
                    }

                    Vector3 topLeft = positions[ToIndex(idxRow, prevCol, cols)];
                    Vector3 topRight = positions[ToIndex(idxRow, curCol, cols)];
                    Vector3 bottomLeft = positions[ToIndex(prevRow, prevCol, cols)];
                    Vector3 bottomRight = positions[ToIndex(prevRow, curCol, cols)];
                    tiles.Add(BuildSphereTile(topLeft, topRight, bottomLeft, bottomRight));
                }
            }

            //create tile tree
            tileTree = DefineTiles.BuildSingleLevelBoundsTree(tiles);
        }

        Mesh BuildSphereTile(Vector3 topLeft, Vector3 topRight, Vector3 bottomLeft, Vector3 bottomRight)
        {
            Mesh tile = new Mesh(hasNormals: true, hasUVs: true);
            tile.Vertices = new List<Vertex>();
            tile.Vertices.Add(new Vertex(topLeft, -Vector3.Normalize(topLeft), Vector4.One, new Vector2(0.0, 1.0)));
            tile.Vertices.Add(new Vertex(topRight, -Vector3.Normalize(topRight), Vector4.One, new Vector2(1.0, 1.0)));
            tile.Vertices.Add(new Vertex(bottomLeft, -Vector3.Normalize(bottomLeft), Vector4.One, new Vector2(0.0, 0.0)));
            tile.Vertices.Add(new Vertex(bottomRight, -Vector3.Normalize(bottomRight), Vector4.One, new Vector2(1.0, 0.0)));

            tile.Faces = new List<Face>();
            tile.Faces.Add(new Face(new int[] { 0, 1, 2 }));
            tile.Faces.Add(new Face(new int[] { 2, 1, 3 }));
            return tile;
        }

        private void BuildTileTextures()
        {
            tileList = new TileList()
            {
                MeshExt = meshExt,
                ImageExt = withTextures ? imageExt : null,
                MeshFrame = meshFrame,
                HasIndexImages = true,
                TilingScheme = TilingScheme.Flat,
                LeafNames = new List<string>(),
                ParentNames = new List<string>()
            };

            if (sceneMesh != null && sceneMesh.Frame != tileList.MeshFrame)
            {
                throw new Exception(string.Format("existing scene mesh in frame {0} but tile list in frame {1}",
                                                  sceneMesh.Frame, tileList.MeshFrame));
            }

            var tilesToTexture = tileTree.DepthFirstTraverse()
                .Where(l => l.HasComponent<MeshImagePair>() && l.GetComponent<MeshImagePair>().Mesh != null)
                .ToList();
            int tileCount = tilesToTexture.Count;

          
            var texMsg = string.Format("{0}x{0} {1} textures{2}",
                                       resolution, options.TextureVariant,
                                       options.TextureVariant != TextureVariant.Original ?
                                       " (falling back to " + TextureVariant.Original + ")" : "");
            pipeline.LogInfo("processing {0} tiles{1}", tileCount, ", backprojecting ");
            pipeline.LogInfo("saving tile backproject index images");

            int np = 0, curTileNum = 0, numFailed = 0, numSucceded = 0;

            void buildTile(SceneNode tile)
            {
                Interlocked.Increment(ref curTileNum);
                Interlocked.Increment(ref np);

                if (!options.NoProgress)
                {
                    pipeline.LogInfo("{0}saving tile {1}/{2} ({3:F2}%){4}: {5}",
                                     withTextures ? "texturing and " : "",
                                     curTileNum, tileCount, 100 * curTileNum / (float)tileCount,
                                     np > 1 ? ", processing " + np + " in parallel" : "", tile.Name);
                }

                MeshImagePair mp = tile.GetComponent<MeshImagePair>();

                Image index = new Image(3, resolution, resolution);
                mp.Image = BackprojectTile(tile, mp.Mesh, index);

                if (mp.Mesh != null && (!withTextures || mp.Image != null))
                {
                    SaveTile(tile.Name, mp.Mesh, mp.Image, index, localSave, cloudSave, tile.IsLeaf);
                    Interlocked.Increment(ref numSucceded);
                }
                else
                {
                    Interlocked.Increment(ref numFailed);
                }

                //conserve memory
                tile.AddComponent(new MeshImagePairStats(tile.GetComponent<MeshImagePair>()));
                tile.RemoveComponent<MeshImagePair>();

                Interlocked.Decrement(ref np);
            }

            contexts = Backproject.BuildContexts(obsToHull, imageObservations, mission, frameCache,
                                                     observationCache, meshFrame, tcopts.UsePriors,
                                                     tcopts.OnlyAligned, msg => pipeline.LogWarn(msg));


            //it used to be the case that it was a perf win to build the tiles serially at least when backprojecting
            //but probably not anymore
            //now that PipelineCore implements locking to prevent multiple threads from trying to load the same image
            CoreLimitedParallel.ForEach(tilesToTexture, buildTile);

            if (withTextures && numFailed > 0)
            {
                pipeline.LogWarn("failed to generate textures for {0} tiles", numFailed);
            }

            pipeline.LogInfo("{0} tiles built successfully", numSucceded);
            tileTree.DumpStats(msg => pipeline.LogInfo(msg));
        }

        private Image BackprojectTile(SceneNode node, Mesh mesh, Image index)
        {
            try
            {
                List<PixelPoint> missingPixels = null;
                missingPixels = new List<PixelPoint>();

                ObsSelectionStrategy obsSelStrat = ObsSelectionStrategy.Create(options.ObsSelectionStrategy);
                obsSelStrat.Initialize(mesh, new MeshOperator(mesh), sceneCaster, contexts, resolution, tcopts.BackprojectQuality);
                IDictionary<Pixel, Backproject.ObsPixel> backprojectResults = BackprojectRoverObservations(mesh, options.TextureResolution, missingPixels,
                                                                    obsSelStrat, debugSubdir: node.Name);

                Image image = new Image(3, resolution, resolution);
                image.Fill(skyColor); //TODO: set to mars sky color

                if (index != null)
                {
                    Backproject.FillIndexImage(backprojectResults, index);
                }

                var stats = Backproject.FillOutputTexture(pipeline, backprojectResults, image, options.TextureVariant,
                                                          options.BackprojectInpaintPixels, fallbackToOriginal: true,
                                                          orbitalTexture: orbitalTexture);
                return image;
            }
            catch (Exception ex)
            {
                pipeline.LogException(ex, $"error backprojecting tile {node.Name}");
                return null;
            }
        }
    }
}