using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Amazon.DynamoDBv2.DataModel;
using log4net;
using Newtonsoft.Json.Linq;
using OPS.Pipeline;
using OPS.Cloud;
using OPS.Geometry;
using OPS.Util;

namespace OPS.Pipeline.TilingServer
{
    public enum TextureMode
    {
        None,
        Clip,       //generate tile textures by clipping regions out of the source texture and offsetting uvs
        Bake,       //generate tile textures by atlassing tiles and sampling source texture at a desired resolution
        Backproject //generate tile textures by choosing the best data from observations that viewed the mesh
    }

    [DynamoDBTable("TilingProjects")]
    [DynamoDBReadCapacity(5, 50)]
    [DynamoDBWriteCapacity(5, 50)]
    public class TilingProject
    {
        public const string DEF_TILESET_MESH_FORMAT = "b3dm";
        public const string DEF_TILESET_IMAGE_FORMAT = "jpg";
        public const string DEF_TILESET_INDEX_FORMAT = "png";

        [DynamoDBHashKey]
        public string Name;

        public TilingScheme TilingScheme;

        public SkirtMode SkirtMode;

        public MeshReconstructionMethod ReconstructionMethod;

        public int FacesPerTile;

        public int TextureResolution;

        public TextureMode TextureMode;

        public bool TilesDefined;

        public PipelineStateMachine.ProjectType ProjectType;

        public bool StartedRunning;

        public bool FinishedRunning;

        public string InputNamesUrl;

        public string NodeIdsUrl;

        public int MaxLeafGroupSize;

        public bool ConvertLinearRGBToSRGB; //applies to tileset and export images, not internal images

        public string ExportDir = "www"; //disable exporting meshes and images if null or empty

        public string ExportMeshFormat = null; //disable exporting meshes if null or empty

        public string ExportImageFormat = null; //disable exporting images if null or empty

        public string ExportIndexFormat = null; //disable exporting indexes if null or empty

        public string InternalTileDir = "tiles"; //disable saving internal tile meshes and images if null or empty

        public string InternalMeshFormat = "ply";

        public string InternalImageFormat = "png";

        public string InternalIndexFormat = "tif"; //tile backproject index images disabled if null

        public string TilesetDir = "www"; //disable saving 3D tiles format tiles if null or empty

        public string TilesetMeshFormat = DEF_TILESET_MESH_FORMAT; //but pointclouds will be saved as pnts

        public string TilesetImageFormat = DEF_TILESET_IMAGE_FORMAT; //jpg or png, will be embedded in b3dm

        public string TilesetIndexFormat = DEF_TILESET_INDEX_FORMAT; //e.g. tiff, png, ppm[z]

        public bool EmbedIndexes = true; //embed tileset indexes in b3dm

        public static string ToExt(string fmt)
        {
            if (string.IsNullOrEmpty(fmt))
            {
                return fmt;
            }
            if (!fmt.StartsWith("."))
            {
                fmt = "." + fmt;
            }
            return fmt.ToLower();
        }

        //This constructor must be public for DynamoDB but should not be used
        public TilingProject() { }

        /// <summary>
        /// Creates Project object locally.  
        /// </summary>
        /// <param name="name">Project names in the database must be unique</param>
        protected TilingProject(string name, TilingScheme tilingScheme, SkirtMode skirtMode,
                                MeshReconstructionMethod reconstructionMethod, int faces,
                                int textureResolution, TextureMode textureMode, 
                                PipelineStateMachine.ProjectType projectType, bool convertLinearRGBToSRGB,
                                string exportMeshFormat, string exportImageFormat, int maxLeafGroupSize)
        {
            Name = name;
            TilingScheme = tilingScheme;
            SkirtMode = skirtMode;
            ReconstructionMethod = reconstructionMethod;
            FacesPerTile = faces;
            TextureResolution = textureResolution;
            TextureMode = textureMode;
            ProjectType = projectType;
            TilesDefined = false;
            ConvertLinearRGBToSRGB = convertLinearRGBToSRGB;
            ExportMeshFormat = exportMeshFormat;
            ExportImageFormat = exportImageFormat;
            MaxLeafGroupSize = maxLeafGroupSize;
            IsValid();
        }

        public static TilingProject Create(PipelineCore pipeline, string name, TilingScheme tilingScheme,
                                           SkirtMode skirtMode, MeshReconstructionMethod reconstructionMethod,
                                           int faces, int textureResolution, TextureMode textureMode,
                                           PipelineStateMachine.ProjectType projectType, bool convertLinearRGBToSRGB,
                                           string exportMeshFormat, string exportImageFormat, int maxLeafGroupSize)
        {
            TilingProject project = new TilingProject(name, tilingScheme, skirtMode, reconstructionMethod, faces,
                                                      textureResolution, textureMode, projectType,
                                                      convertLinearRGBToSRGB, exportMeshFormat, exportImageFormat,
                                                      maxLeafGroupSize);
            project.Save(pipeline);
            return project;
        }

        public static TilingProject Find(PipelineCore pipeline, string name)
        {
            TilingProject project = pipeline.LoadDatabaseItem<TilingProject>(name);
            if (project != null)
            {
                project.IsValid();
            }
            return project;
        }

        public static IEnumerable<TilingProject> FindAll(PipelineCore pipeline, ILog logger = null)
        {
            return pipeline.ScanDatabase<TilingProject>();
        }

        public void Save(PipelineCore pipeline)
        {
            IsValid();
            pipeline.SaveDatabaseItem(this);
        }

        public const int SLEEP_BETWEEN_NODE_DELETES_MS = 10;
        public void Delete(PipelineCore pipeline, bool ignoreErrors = true, ISet<string> keepMeshes = null, bool keepTileset = false)
        {
            var nodes = TilingNode.Find(pipeline, this, pipeline.Logger, ignoreErrors);
            int nn = nodes.Count();
            int n = 0; 
            pipeline.LogInfo("deleting {0} nodes", nn);
            foreach (var node in nodes)
            {
                node.Delete(pipeline, ignoreErrors, keepMeshes);
                if (pipeline is CloudPipeline)
                {
                    Thread.Sleep(SLEEP_BETWEEN_NODE_DELETES_MS); //throttle to reduce chance of exponential backoff
                }
                if (++n % 500 == 0)
                {
                    pipeline.LogInfo("deleted {0} nodes", n);
                }
            }

            var inputNames = LoadInputNames(pipeline);
            pipeline.LogInfo("deleting {0} inputs", inputNames.Count());
            foreach (var inputName in inputNames)
            {
                var input = TilingInput.Find(pipeline, Name, inputName);
                if (input != null)
                {
                    input.Delete(pipeline, ignoreErrors, keepMeshes);
                }
            }

            pipeline.DeleteCacheFolder(Name);

            if (!string.IsNullOrEmpty(ExportDir))
            {
                //trailing slash is necessary to make sure we don't delete foo_bar/* in addition to foo/*
                var baseUrl = StringHelper.EnsureTrailingSlash(pipeline.GetStorageUrl(ExportDir, Name));
                pipeline.LogInfo("deleting tileset exports under {0}", baseUrl);
                pipeline.DeleteFiles(baseUrl, "*", ignoreErrors);
            }

            if (!keepTileset && !string.IsNullOrEmpty(TilesetDir) && TilesetDir != ExportDir && TilesetDir != InternalTileDir)
            {
                //trailing slash is necessary to make sure we don't delete foo_bar/* in addition to foo/*
                var baseUrl = StringHelper.EnsureTrailingSlash(pipeline.GetStorageUrl(TilesetDir, Name));
                pipeline.LogInfo("deleting tileset under {0}", baseUrl);
                pipeline.DeleteFiles(baseUrl, "*", ignoreErrors);
            }

            if (!string.IsNullOrEmpty(NodeIdsUrl))
            {
                pipeline.LogInfo("deleting node ids");
                pipeline.DeleteFile(NodeIdsUrl, ignoreErrors);
            }

            if (!string.IsNullOrEmpty(InputNamesUrl))
            {
                pipeline.LogInfo("deleting input names");
                pipeline.DeleteFile(InputNamesUrl, ignoreErrors);
            }

            pipeline.DeleteDatabaseItem(this, ignoreErrors);
        }

        private void IsValid()
        {
            if (Name == null)
            {
                throw new Exception("TilingProject is missing a required field");
            }
            if (TextureMode == TextureMode.Backproject)
            {
                throw new Exception("unsupported texture mode: " + TextureMode);
            }
        }

        public List<string> LoadNodeIds(PipelineCore pipeline)
        {
            return LoadStringArray(NodeIdsUrl, pipeline);
        }

        public string SaveNodeIds(List<string> ids, PipelineCore pipeline)
        {
            var url = pipeline.GetStorageUrl(InternalTileDir, Name, "nodeids.json");
            SaveStringArray(url, ids, pipeline);
            NodeIdsUrl = url;
            return url;
        }

        public List<string> LoadInputNames(PipelineCore pipeline)
        {
            return LoadStringArray(InputNamesUrl, pipeline);
        }

        public string SaveInputNames(List<string> names, PipelineCore pipeline)
        {
            var url = pipeline.GetStorageUrl(InternalTileDir, Name, "inputnames.json");
            SaveStringArray(url, names, pipeline);
            InputNamesUrl = url;
            return url;
        }

        private List<string> LoadStringArray(string url, PipelineCore pipeline)
        {
            List<string> ret = new List<string>();
            if (!string.IsNullOrEmpty(url))
            {
                if (pipeline.FileExists(url))
                {
                    pipeline.GetFile(url, f =>
                    {
                        var txt = File.ReadAllText(f);
                        ret = ((JArray)JsonHelper.FromJson(txt, autoTypes: false)).ToObject<List<string>>();
                    });
                }
                else
                {
                    pipeline.LogWarn("{0} not found", url);
                }
            }
            return ret;
        }

        private void SaveStringArray(string url, List<string> strings, PipelineCore pipeline)
        {
            TemporaryFile.GetAndDelete(".json", tmpJson =>
            {
                File.WriteAllText(tmpJson, JsonHelper.ToJson(strings, autoTypes: false));
                pipeline.SaveFile(tmpJson, url);
            });
        }
    }
}
