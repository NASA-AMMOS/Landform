using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Amazon.DynamoDBv2.DataModel;
using log4net;
using Newtonsoft.Json.Linq;
using OPS.Util;
using OPS.Cloud;
using OPS.Geometry;
using OPS.Pipeline;

namespace OPS.Pipeline.TilingServer
{
    [DynamoDBTable("TilingProjects")]
    [DynamoDBReadCapacity(5, 50)]
    [DynamoDBWriteCapacity(5, 50)]
    public class TilingProject
    {
        [DynamoDBHashKey]
        public string Name;

        public ProjectType ProjectType;

        public string ProductPath;

        public TilingScheme TilingScheme = TilingDefaults.TILING_SCHEME;

        public int MaxFacesPerTile = TilingDefaults.MAX_FACES_PER_TILE;

        public MeshReconstructionMethod ParentReconstructionMethod = TilingDefaults.PARENT_RECONSTRUCTION_METHOD;

        public SkirtMode SkirtMode = TilingDefaults.SKIRT_MODE;

        public TextureMode TextureMode = TilingDefaults.TEXTURE_MODE;

        public int MaxTextureResolution = TilingDefaults.MAX_TEXTURE_RESOLUTION;

        public double MaxTexelsPerMeter = TilingDefaults.MAX_TEXELS_PER_METER;

        public double MaxTextureStretch = TilingDefaults.MAX_TEXTURE_STRETCH;

        public bool PowerOfTwoTextures = TilingDefaults.POWER_OF_TWO_TEXTURES;

        public bool TilesDefined;

        public bool StartedRunning;

        public bool FinishedRunning;

        public string InputNamesUrl;

        public string NodeIdsUrl;

        public string ExportDir = TilingDefaults.EXPORT_DIR; //disable exporting meshes and images if null or empty

        public string ExportMeshFormat = null; //disable exporting meshes if null or empty

        public string ExportImageFormat = null; //disable exporting images if null or empty

        public string InternalTileDir = TilingDefaults.INTERNAL_TILE_DIR; //disable saving internal tile meshes and images if null or empty

        public string InternalMeshFormat = TilingDefaults.INTERNAL_MESH_FORMAT;

        public string InternalImageFormat = TilingDefaults.INTERNAL_IMAGE_FORMAT;

        public string TilesetDir = TilingDefaults.TILESET_DIR; //disable saving 3D tiles format tiles if null or empty

        public string TilesetMeshFormat = TilingDefaults.TILESET_MESH_FORMAT; //but pointclouds will be saved as pnts

        public string TilesetImageFormat = TilingDefaults.TILESET_IMAGE_FORMAT; //jpg or png, will be embedded in b3dm

        public Guid TextureProjectorGuid;

        public static string ToExt(string fmt)
        {
            if (!fmt.StartsWith("."))
            {
                fmt = "." + fmt;
            }
            return fmt.ToLower();
        }

        //This constructor must be public for DynamoDB but should not be used
        public TilingProject() { }

        protected TilingProject(string name, ProjectType projectType, string productPath)
        {
            Name = name;
            ProjectType = projectType;
            ProductPath = productPath;
            IsValid();
        }

        public static TilingProject Create(PipelineCore pipeline, string name, ProjectType projectType,
                                           string productPath)
        {
            TilingProject project = new TilingProject(name, projectType, productPath);
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
        public void Delete(PipelineCore pipeline, bool ignoreErrors = true, ISet<string> keepMeshes = null)
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

            if (!string.IsNullOrEmpty(TilesetDir) && TilesetDir != ExportDir && TilesetDir != InternalTileDir)
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
                pipeline.GetFile(url, f =>
                {
                    ret = ((JArray)JsonHelper.FromJson(File.ReadAllText(f), autoTypes: false)).ToObject<List<string>>();
                });
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
