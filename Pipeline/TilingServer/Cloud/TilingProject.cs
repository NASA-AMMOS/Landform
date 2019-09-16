using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using Amazon.DynamoDBv2.DataModel;
using log4net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OPS.Cloud;
using OPS.Util;
using OPS.Geometry;
using OPS.Pipeline.AlignmentServer;

namespace OPS.Pipeline.TilingServer
{
    [DynamoDBTable("TilingProjects")]
    [DynamoDBReadCapacity(5, 50)]
    [DynamoDBWriteCapacity(5, 50)]
    public class TilingProject
    {
        [DynamoDBHashKey]
        public string Name;

        public string TilingScheme;

        public string SkirtMode;

        public string ReconMethod;

        public int FacesPerTile;

        public int TileResolution;

        public bool TilesDefined;

        public string ProjectType;

        public bool StartedRunning;

        public bool FinishedRunning;

        public HashSet<string> InputNames = new HashSet<string>(); //MT safety: lock before accessing

        public string NodeIdsUrl;

        public string ExportMeshFormat;

        public string ExportImageFormat;

        public int MaxLeafGroupSize;

        //This constructor must be public for DynamoDB but should not be used
        public TilingProject() { }

        /// <summary>
        /// Creates Project object locally.  
        /// </summary>
        /// <param name="name">Project names in the database must be unique</param>
        protected TilingProject(string name, TilingScheme tilingScheme, SkirtMode skirtMode,
                                MeshReconMethod reconMethod, int faces, int resolution, string projectType,
                                string exportMeshFormat, string exportImageFormat, int maxLeafGroupSize)
            : this()
        {
            Name = name;
            TilingScheme = tilingScheme.ToString();
            SkirtMode = skirtMode.ToString();
            ReconMethod = reconMethod.ToString();
            FacesPerTile = faces;
            TileResolution = resolution;
            ProjectType = projectType;
            TilesDefined = false;
            ExportMeshFormat = exportMeshFormat;
            ExportImageFormat = exportImageFormat;
            MaxLeafGroupSize = maxLeafGroupSize;
            IsValid();
        }


        public static TilingProject Create(PipelineCore pipeline, string name, TilingScheme tilingScheme,
                                           SkirtMode skirtMode, MeshReconMethod reconMethod, int faces, int resolution,
                                           string projectType, string exportMeshFormat, string exportImageFormat,
                                           int maxLeafGroupSize)
        {
            TilingProject project = new TilingProject(name, tilingScheme, skirtMode, reconMethod, faces, resolution,
                                                      projectType, exportMeshFormat, exportImageFormat,
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
        public void Delete(PipelineCore pipeline, bool ignoreErrors = true)
        {
            if (StartedRunning)
            {
                var nodes = TilingNode.Find(pipeline, this, pipeline.Logger);
                int nn = nodes.Count();
                int n = 0; 
                pipeline.LogInfo("deleting {0} nodes", nn);
                foreach (var node in nodes)
                {
                    node.Delete(pipeline, ignoreErrors);
                    Thread.Sleep(SLEEP_BETWEEN_NODE_DELETES_MS); //throttle to reduce chance of exponential backoff
                    if (++n % 500 == 0)
                    {
                        pipeline.LogInfo("deleted {0} nodes", n);
                    }
                }
            }
            else
            {
                pipeline.LogInfo("deleting 0 nodes - project never run");
            }

            var inputs = TilingInput.Find(pipeline, this, pipeline.Logger);
            pipeline.LogInfo("deleting {0} inputs", inputs.Count());
            foreach (var input in inputs)
            {
                if (input != null)
                {
                    input.Delete(pipeline, ignoreErrors);
                }
            }

            pipeline.DeleteProjectCache(Name);

            pipeline.DeleteFiles(pipeline.GetStorageUrl("www", Name), "*", ignoreErrors);

            if (!string.IsNullOrEmpty(NodeIdsUrl))
            {
                pipeline.DeleteFile(NodeIdsUrl, ignoreErrors);
            }

            pipeline.DeleteDatabaseItem(this, ignoreErrors);
        }

        private void IsValid()
        {
            if (!(Name != null && TilingScheme != null && SkirtMode != null))
            {
                throw new Exception("TilingProject is missing a required field");
            }
        }

        public TilingScheme GetTilingScheme()
        {
            return (TilingScheme)Enum.Parse(typeof(TilingScheme), TilingScheme, true);
        }

        public SkirtMode GetSkirtMode()
        {
            return (SkirtMode)Enum.Parse(typeof(SkirtMode), SkirtMode, true);
        }

        public MeshReconMethod GetReconMethod()
        {
            return (MeshReconMethod)Enum.Parse(typeof(MeshReconMethod), ReconMethod, true);
        }

        public List<string> LoadNodeIds(PipelineCore pipeline)
        {
            List<string> ids = null;
            if (!string.IsNullOrEmpty(NodeIdsUrl))
            {
                pipeline.GetFile(NodeIdsUrl, f =>
                {
                    ids = ((JArray)JsonHelper.FromJson(File.ReadAllText(f), autoTypes: false)).ToObject<List<string>>();
                });
            }
            return ids;
        }

        public string SaveNodeIds(List<string> ids, PipelineCore pipeline)
        {
            var url = pipeline.GetStorageUrl("tile", Name, "nodeids.json");
            TemporaryFile.GetAndDelete(".json", tmpJson =>
            {
                var json = JsonHelper.ToJson(ids, autoTypes: false);
                File.WriteAllText(tmpJson, json);
                pipeline.SaveFile(tmpJson, url);
            });
            NodeIdsUrl = url;
            return url;
        }
    }
}
