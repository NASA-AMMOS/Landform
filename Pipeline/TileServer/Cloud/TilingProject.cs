using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using Amazon.DynamoDBv2.DataModel;
using OPS.Cloud;
using OPS.Util;
using OPS.Geometry;
using OPS.Plumbing;
using log4net;
using Newtonsoft.Json.Linq;

namespace OPS.Pipeline.TileServer
{

    [DynamoDBTable("TilingProjects")]
    [DynamoDBReadCapacity(5, 50)]
    [DynamoDBWriteCapacity(5, 50)]
    public class TilingProject
    {
        [DynamoDBHashKey] //Partition key
        [DynamoDBProperty()]
        public string Name { get; set; }

        public string TilingScheme { get; set; }
        public string SkirtMode { get; set; }
        public string ReconMethod { get; set; }

        public int FacesPerTile { get; set; }
        public int TileResolution { get; set; }

        public bool TilesDefined { get; set; }

        public string ProjectType { get; set; }

        public bool StartedRunning { get; set; }

        public bool FinishedRunning { get; set; }

        public List<string> InputNames { get; set; }

        public string NodeIdsUrl { get; set; }

        public string ExportMeshFormat { get; set; }

        public string ExportImageFormat { get; set; }

        public int MaxLeafGroupSize { get; set; }

        public TilingProject()
        {
        }

        /// <summary>
        /// Creates Project object locally.  
        /// </summary>
        /// <param name="name">Project names in the database must be unique</param>
        protected TilingProject(string name, TilingScheme tilingScheme, SkirtMode skirtMode,
                                MeshReconMethod reconMethod, int faces, int resolution, string projectType,
                                string exportMeshFormat, string exportImageFormat, int maxLeafGroupSize)
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


        public static TilingProject Create(DynamoDBContext context, string name, TilingScheme tilingScheme,
                                           SkirtMode skirtMode, MeshReconMethod reconMethod, int faces, int resolution,
                                           string projectType, string exportMeshFormat, string exportImageFormat,
                                           int maxLeafGroupSize)
        {
            TilingProject project = new TilingProject(name, tilingScheme, skirtMode, reconMethod, faces, resolution,
                                                      projectType, exportMeshFormat, exportImageFormat,
                                                      maxLeafGroupSize);
            project.Save(context);
            return project;
        }

        public static TilingProject Find(DynamoDBContext context, string name)
        {
            TilingProject project = context.Load<TilingProject>(name);
            if (project != null)
            {
                project.IsValid();
            }
            return project;
        }

        public static IEnumerable<TilingProject> FindAll(DynamoDBContext context, ILog logger = null)
        {
            return DBUtil.Scan<TilingProject>(context, logger);
        }

        public void Save(DynamoDBContext context)
        {
            IsValid();
            context.Save(this);
        }

        public const int SLEEP_BETWEEN_NODE_DELETES_MS = 10;
        public void Delete(PipelineCore pipeline, bool ignoreErrors = true)
        {
            if (StartedRunning)
            {
                var nodes = TilingNode.Find(pipeline, this, pipeline.Logger);
                int nn = nodes.Count();
                int n = 0; 
                pipeline.Logger.Info("deleting " + nn + " nodes");
                foreach (var node in nodes)
                {
                    node.Delete(pipeline, ignoreErrors);
                    Thread.Sleep(SLEEP_BETWEEN_NODE_DELETES_MS); //throttle to reduce chance of exponential backoff
                    if (++n % 500 == 0)
                    {
                        pipeline.Logger.Info("deleted " + n + " nodes");
                    }
                }
            }
            else
            {
                pipeline.Logger.Info("deleting 0 nodes - project never run");
            }

            var inputs = TilingInput.Find(pipeline.DynamoContext, this, pipeline.Logger);
            pipeline.Logger.Info("deleting " + inputs.Count() + " inputs");
            foreach (var input in inputs)
            {
                input.Delete(pipeline, ignoreErrors);
            }

            pipeline.DeleteProjectCache(Name);

            string wwwS3Url = TileServerConfig.Instance.WWWUrl(Name);
            pipeline.Storage(wwwS3Url).DeleteObjects(wwwS3Url, ignoreErrors: ignoreErrors, logger: pipeline.Logger);

            if (!string.IsNullOrEmpty(NodeIdsUrl))
            {
                pipeline.Storage(NodeIdsUrl).DeleteObjects(NodeIdsUrl, ignoreErrors: ignoreErrors, logger: pipeline.Logger);
            }

            pipeline.DeleteDynamoItem(this, ignoreErrors);
        }

        private void IsValid()
        {
            if (!(Name != null && TilingScheme != null && SkirtMode != null))
            {
                throw new CloudException("TilingProject is missing a required field");
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
                TemporaryFile.GetAndDelete(".json", tmpJson =>
                        {
                            pipeline.Storage(NodeIdsUrl).DownloadFile(NodeIdsUrl, tmpJson);
                            var json = File.ReadAllText(tmpJson);
                            ids = ((JArray)JsonHelper.FromJson(json, autoTypes: false)).ToObject<List<string>>();
                        });
            }
            return ids;
        }

        public string SaveNodeIds(List<string> ids, PipelineCore pipeline)
        {
            var url = TileServerConfig.Instance.TileUrl(Name, "nodeids.json");
            TemporaryFile.GetAndDelete(".json", tmpJson =>
            {
                var json = JsonHelper.ToJson(ids, autoTypes: false);
                File.WriteAllText(tmpJson, json);
                pipeline.Storage(url).UploadFile(tmpJson, url);
            });
            NodeIdsUrl = url;
            return url;
        }
    }
}
