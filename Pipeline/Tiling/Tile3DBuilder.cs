using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using JPLOPS.Geometry;
using JPLOPS.Util;
using JPLOPS.Imaging;
using JPLOPS.Pipeline.AlignmentServer;

namespace JPLOPS.Pipeline
{
    public static class Tile3DBuilder
    {
        public static readonly string[] SUPPORTED_INDEX_FORMATS = new string[] { "tif", "tiff", "ppm", "ppmz", "png" };

        public static Matrix MakeCesiumHackRootTransform()
        {
            // Put together a hack matrix for viewing in cesium
            Matrix m = new Matrix(96.86356343768793, 24.848542777253734, 0, 0,
                                  -15.986465724980844, 62.317780594908875, 76.5566922962899, 0,
                                  19.02322243409411, -74.15554020821229, 64.3356267137516, 0,
                                  1215107.7612304366, -4736682.902037748, 4081926.095098698, 1);
            Vector3 scale;
            Quaternion q;
            Vector3 trans;
            m.Decompose(out scale, out q, out trans);
            scale = new Vector3(1, 1, 1);
            Matrix rot = Matrix.CreateRotationX(MathHelper.ToRadians(90)) * Matrix.CreateFromQuaternion(q);
            return Matrix.CreateScale(scale) * rot * Matrix.CreateTranslation(trans);
        }

        public static Tile3D.Tileset BuildTileset(SceneNode root, Func<SceneNode, string> nodeToUrl,
                                                  Matrix? rootTransform = null)
        {
            Dictionary<SceneNode, Tile3D.Tile> nodesToTiles = new Dictionary<SceneNode, Tile3D.Tile>();

            foreach (SceneNode curNode in root.DepthFirstTraverse())
            {
                if (!nodesToTiles.ContainsKey(curNode))
                {
                    var tile = SceneNodeToTile(curNode, nodeToUrl);
                    nodesToTiles.Add(curNode, tile);
                    if (curNode.Transform.Parent != null) // should only be null for root node
                    {
                        nodesToTiles[curNode.Transform.Parent.Node].Children.Add(tile);
                    }
                }
            }

            var tileset = new Tile3D.Tileset();

            tileset.Root = nodesToTiles[root];
            tileset.Root.Transform = MatrixToList(rootTransform.HasValue ? rootTransform.Value : Matrix.Identity);
            tileset.GeometricError = root.GetOrAddComponent<NodeBounds>().Bounds.MaxDimension(); //default 0

            tileset.Asset.GLTFUpAxis = "z";

            return tileset;
        }

        public static void BuildAndSaveTileset(PipelineCore pipeline, SceneNode root, string tilesetDir,
                                               string tilesetName, Func<SceneNode, string> nodeToUrl,
                                               Matrix? rootTransform = null, Action<string> info = null)
        {
            info = info ?? (msg => pipeline.LogInfo(msg));

            info("building tileset json");
            var tileset = BuildTileset(root, nodeToUrl, rootTransform);

            string tilesetUrl = pipeline.GetStorageUrl(tilesetDir, tilesetName, "tileset.json");

            info($"saving tileset json to {tilesetUrl}");
            TemporaryFile.GetAndDelete(".json", f =>
            {
                File.WriteAllText(f, JsonConvert.SerializeObject(tileset, Formatting.None));
                pipeline.SaveFile(f, tilesetUrl);
            });

            //some GDS testcases are based on seeing tileset bounds size in log spew
            var rootSize = root.GetComponent<NodeBounds>().Bounds.Extent(); //BuildTileset() ensures root has bounds
            info($"tileset bounds (meters): {rootSize.X:F3}x{rootSize.Y:F3}x{rootSize.Z:F3}");

            var sb = new StringBuilder();
            root.DumpStats(msg =>
            {
                sb.Append(msg);
                sb.Append("\n");
                info(msg);
            });

            var statsUrl = pipeline.GetStorageUrl(tilesetDir, tilesetName, "stats.txt");
            info($"saving tileset stats to {statsUrl}");
            TemporaryFile.GetAndDelete(".txt", f =>
            {
                File.WriteAllText(f, sb.ToString());
                pipeline.SaveFile(f, statsUrl);
            });
        }

        //convert existing tile meshes, images, and indices to 3DTiles format
        //input data is read from MeshImagePair node components where available (in core workflow)
        //or from project storage in folder <inputDir> otherwise (out of core workflow)
        //nodes with content are marked with MeshImagePair or MeshImagePairStats (for out of core workflow)
        //output is written to project storage in folder <tilesetDir>/<tilesetName>/
        public static void ConvertTiles(PipelineCore pipeline, SceneNode root, string inputDir, string tilesetDir,
                                        string tilesetName, bool withTextures = true, bool withIndices = false,
                                        bool embedIndices = true, string inMeshExt = "ply", string inImgExt = "png",
                                        string inIdxExt = "tiff", string tsMeshExt = "b3dm", string tsImgExt = "jpg",
                                        string tsIdxExt = "ppmz", bool convertLinearRGBToSRGB = true,
                                        Action<string> info = null)
        {
            info = info ?? (msg => pipeline.LogInfo(msg));

            Func<string, string> toExt = str => !string.IsNullOrEmpty(str) ? "." + str.ToLower().TrimStart('.') : null;

            inMeshExt = toExt(inMeshExt);
            inImgExt = toExt(inImgExt);
            inIdxExt = toExt(inIdxExt);

            tsMeshExt = toExt(tsMeshExt);
            tsImgExt = toExt(tsImgExt);
            tsIdxExt = toExt(tsIdxExt);

            bool embedImgs = tsMeshExt == ".b3dm";

            string idxSfx = TilingDefaults.INDEX_FILE_SUFFIX;

            var tsExts = new List<string>() { tsMeshExt, tsImgExt };
            if (withIndices)
            {
                tsExts.Add(tsIdxExt);
            }

            string inputUrl(SceneNode tile, string ext, string sfx = "")
            {
                var url = pipeline.GetStorageUrl(inputDir, tile.Name + sfx + ext);
                return pipeline.FileExists(url) ? url : null;
            }

            string outputUrl(SceneNode tile, string ext, string sfx = "")
            {
                return pipeline.GetStorageUrl(tilesetDir, tilesetName, tile.Name + sfx + ext);
            }

            Image loadImage(SceneNode tile, string ext, string sfx = "", string what = "image")
            {
                var url = inputUrl(tile, ext, sfx);
                if (url == null)
                {
                    pipeline.LogWarn("no {0} found for tile {1}", what, tile.Name);
                    return null;
                }
                return pipeline.LoadImage(url, noCache: true);
            }

            //MeshImagePair may have been replaced with MeshImagePairStats to save memory
            var meshNodes = root.DepthFirstTraverse()
                .Where(l => l.HasComponent<MeshImagePair>() || l.HasComponent<MeshImagePairStats>())
                .ToList();

            info($"saving {meshNodes.Count} {tsMeshExt} tiles" + (withTextures ? $" with {tsImgExt} textures" : "") +
                 (withIndices ? $" and {tsIdxExt} indices" : ""));

            CoreLimitedParallel.ForEach(meshNodes, tile =>
            {
                TemporaryFile.GetAndDeleteMultiple(tsExts.ToArray(), tmpFiles =>
                {
                    string tmpMesh = tmpFiles[0], tmpImg = tmpFiles[1], tmpIdx = withIndices ? tmpFiles[2] : null;

                    var mip = tile.GetComponent<MeshImagePair>();

                    //load mesh first to make sure it exists, but save it last after image and index are converted
                    Mesh mesh = mip?.Mesh;
                    if (mesh == null)
                    {
                        var meshUrl = inputUrl(tile, inMeshExt);
                        if (meshUrl != null)
                        {
                            mesh = Mesh.Load(pipeline.GetFileCached(meshUrl, "meshes"));
                        }
                    }
                    if (mesh == null)
                    {
                        pipeline.LogWarn("skipping tile {0}, no mesh found", tile.Name);
                        return;
                    }

                    Image image = withTextures ? (mip?.Image ?? loadImage(tile, inImgExt)) : null;
                    if (image != null)
                    {
                        if (convertLinearRGBToSRGB)
                        {
                            image = image.LinearRGBToSRGB();
                        }
                        image.Save<byte>(tmpImg); //convert to tileset image format
                        if (!embedImgs)
                        {
                            pipeline.SaveFile(tmpImg, outputUrl(tile, tsImgExt));
                        }
                    }

                    Image index = withIndices ? (mip?.Index ?? loadImage(tile, inIdxExt, idxSfx, "index")) : null;
                    if (index != null)
                    {
                        SaveTileIndex(index, tmpIdx, msg => pipeline.LogVerbose($"{msg} for tile {tile.Name}"));
                        if (!embedImgs || !embedIndices)
                        {
                            pipeline.SaveFile(tmpIdx, outputUrl(tile, tsIdxExt, idxSfx));
                            tmpIdx = null; //don't also try to save index with mesh in this case
                        }
                    }

                    //for b3dm the image and index files will be read and embedded
                    //for other mesh formats they might be referenced by name
                    mesh.Save(tmpMesh, image != null ? tmpImg : null, index != null ? tmpIdx : null);
                    pipeline.SaveFile(tmpMesh, outputUrl(tile, tsMeshExt));
                });
            });
        }

        //save a tile index image
        //implies storage format from the file extension
        //if the format supports float (currently only tiff) then the index is saved directly
        //if the format supports 16 bit (currently ppm, ppmz, and png) then orbital and invalid indices are removed
        //if the format only supports less than 16 bit, only issues a warning and skips storing index
        public static void SaveTileIndex(Image index, string file, Action<string> warn = null)
        {
            warn = warn ?? (msg => {});

            string ext = Path.GetExtension(file).ToLower();
            if (ext == ".tif" || ext == ".tiff")
            {
                var opts = new GDALTIFFWriteOptions(GDALTIFFWriteOptions.CompressionType.DEFLATE);
                var serializer = new GDALSerializer(opts);
                PathHelper.EnsureExists(Path.GetDirectoryName(file));
                serializer.Write<float>(file, index, ImageConverters.PassThrough);
            }
            else if (ext == ".ppm" || ext == ".ppmz" || ext == ".png")
            {
                //these formats support 16 bit color components
                //we can generally save surface observation image indices
                //but orbital can easily have larger dimensions than 65535
                int numBad = 0;
                index = new Image(index);
                for (int r = 0; r < index.Height; r++)
                {
                    for (int c = 0; c < index.Width; c++)
                    {
                        bool bad = false;
                        for (int b = 0; b < index.Bands; b++)
                        {
                            if (index[b, r, c] < 0 || index[b, r, c] > 65535)
                            {
                                bad = true;
                                break;
                            }
                        }
                        if (bad)
                        {
                            ++numBad;
                            index[0, r, c] = Observation.NO_OBSERVATION_INDEX;
                            for (int b = 1; b < index.Bands; b++)
                            {
                                index[b, r, c] = 0;
                            }
                        }
                    }
                }
                if (numBad > 0)
                {
                    warn($"cleared {numBad} out of range or orbital pixels saving index image to 16 bit {ext}");
                }
                PathHelper.EnsureExists(Path.GetDirectoryName(file));
                index.Save<ushort>(file, ImageConverters.PassThrough);
            }
            else
            {
                warn($"not saving index image, {ext} does not support 16 bit");
            }
        }

        public static bool CheckIndexFormat(string fmt)
        {
            return SUPPORTED_INDEX_FORMATS.Contains(fmt.ToLower().TrimStart('.'));
        }

        public static List<double> MatrixToList(Matrix m)
        {            
            //3DTiles uses column matrices in column major order
            //http://docs.opengeospatial.org/cs/18-053r2/18-053r2.html#37
            //XNA Matrix is a row matrix in row major order
            //https://docs.microsoft.com/en-us/previous-versions/windows/silverlight/dotnet-windows-silverlight/bb197911(v=xnagamestudio.35)#remarks
            //row major -> column major is a transpose
            //but row matrix -> column matrix is also a transpose
            //so the transposes cancel out
            return new double[]
            {
                m.M11, m.M12, m.M13, m.M14,
                m.M21, m.M22, m.M23, m.M24,
                m.M31, m.M32, m.M33, m.M34,
                m.M41, m.M42, m.M43, m.M44
            }.ToList();
        }

        public static Matrix ListToMatrix(List<double> list)
        {
           return new Matrix(list[0], list[1], list[2], list[3],
                             list[4], list[5], list[6], list[7],
                             list[8], list[9], list[10], list[11],
                             list[12], list[13], list[14], list[15]);
        }

        /// <summary>
        /// Converts an AABB to a 3D Tiles Box bound array
        /// </summary>
        /// <param name="b"></param>
        /// <returns></returns>
        public static List<double> BoundsToBox(BoundingBox b)
        {
            // "description" : "An array of 12 numbers that define an oriented bounding box.  
            // The first three elements define the x, y, and z values for the center of the box.  
            // The next three elements (with indices 3, 4, and 5) define the x axis direction and half-length.  
            // The next three elements (indices 6, 7, and 8) define the y axis direction and half-length.  
            // The last three elements (indices 9, 10, and 11) define the z axis direction and half-length.",
            Vector3 halfExtent = b.Extent()/2;
            Vector3 center = b.Center();
            Vector3 xaxis = new Vector3(halfExtent.X, 0, 0);
            Vector3 yaxis = new Vector3(0, halfExtent.Y, 0);
            Vector3 zaxis = new Vector3(0, 0, halfExtent.Z);
            var box = new double[]
            {
                center.X, center.Y, center.Z,
                xaxis.X, xaxis.Y, xaxis.Z,
                yaxis.X, yaxis.Y, yaxis.Z,
                zaxis.X, zaxis.Y, zaxis.Z
            };
            return new List<double>(box);
        }

        public static BoundingBox BoxToBounds(List<double> box)
        {
            Vector3 center = new Vector3(box[0], box[1], box[2]);
            Vector3 halfX = new Vector3(box[3], box[4], box[5]);
            Vector3 halfY = new Vector3(box[6], box[7], box[8]);
            Vector3 halfZ = new Vector3(box[9], box[10], box[11]);
            Vector3 min = center - halfX - halfY - halfZ;
            Vector3 max = center + halfX + halfY + halfZ;
            return new BoundingBox(min, max);
        }

        /// <summary>
        /// Create a 3DTile for a node
        /// </summary>
        /// <param name="node"></param>
        /// <param name="nodeToUrl"></param>
        /// <returns></returns>
        public static Tile3D.Tile SceneNodeToTile(SceneNode node, Func<SceneNode, string> nodeToUrl)
        {
            Tile3D.Tile tile = new Tile3D.Tile();
            tile.BoundingVolume.Box = BoundsToBox(node.GetOrAddComponent<NodeBounds>().Bounds);
            tile.Refine = Tile3D.TileRefine.REPLACE;
            if (node.HasComponent<MeshImagePair>() || node.HasComponent<MeshImagePairStats>())
            {
                tile.Content = new Tile3D.TileContent();
                tile.Content.Uri = nodeToUrl(node);
            }
            if (node.HasComponent<NodeGeometricError>())
            {
                tile.GeometricError = node.GetComponent<NodeGeometricError>().Error;
            }
            return tile;
        }
    }
}
