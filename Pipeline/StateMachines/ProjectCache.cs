using System.Collections.Generic;
using System.Linq;
using Amazon.DynamoDBv2.DataModel;
using log4net;
using System.Diagnostics;

namespace OPS.Pipeline.TileServer
{
    public class ProjectCache
    {
        PipelineCore pipeline;
        private string projectName;
        private ILog logger;

        private bool initialized;

        private string rootId;
        private HashSet<string> ids;
        private Dictionary<string, List<string>> dependedOnBy;
        private Dictionary<string, List<string>> dependsOn;
        private HashSet<string> completed;
        private HashSet<string> enqued;
        private HashSet<string> inputsToChunk;

        public ProjectCache(PipelineCore pipeline, string projectName, ILog logger)
        {
            this.pipeline = pipeline;
            this.projectName = projectName;
            this.logger = logger;
            Reset();
        }

        public void Reset()
        {
            initialized = false;
            rootId = null;
            ids = new HashSet<string>();
            dependedOnBy = new Dictionary<string, List<string>>();
            dependsOn = new Dictionary<string, List<string>>();
            completed = new HashSet<string>();
            enqued = new HashSet<string>();
            inputsToChunk = new HashSet<string>();
        }
                
        private void EnsureInitialized()
        {
            if (initialized)
            {
                return;
            }

            if (logger != null)
            {
                logger.InfoFormat("[{0}] initializing project cache", projectName);
            }

            Stopwatch sw = new Stopwatch();
            sw.Start();

            var project = TilingProject.Find(pipeline, projectName);
            if (project == null || !project.TilesDefined)
            {
                throw new System.Exception("cannot initialize cache for " + projectName +
                                           ": project not found or tiles not defined yet");
            }

            var nodes = TilingNode.Find(pipeline, project).ToList();
            foreach (var n in nodes)
            {
                ids.Add(n.Id);
                
                if (n.DependedOnBy == null)
                {
                    n.DependedOnBy = new List<string>();
                }
                if (n.DependsOn == null)
                {
                    n.DependsOn = new List<string>();
                }
                dependedOnBy.Add(n.Id, n.DependedOnBy);
                dependsOn.Add(n.Id, n.DependsOn);
                if (n.MeshUrl != null)
                {
                    completed.Add(n.Id);
                }
                if (n.ParentId == null)
                {
                    rootId = n.Id;
                }
            }

            if (logger != null)
            {
                logger.InfoFormat("[{0}] initialized project cache in {1}s",
                                  projectName, 0.001 * sw.ElapsedMilliseconds);
            }

            initialized = true;
        }

        public string RootId()
        {
            EnsureInitialized();
            return rootId;
        }

        public void MarkEnqueued(string id)
        {
            EnsureInitialized();
            enqued.Add(id);
        }

        public void MarkDone(string id)
        {
            EnsureInitialized();
            completed.Add(id);
        }

        public bool AlreadyProcessed(string id)
        {
            EnsureInitialized();
            return enqued.Contains(id) || completed.Contains(id);
        }

        public bool AlreadyCompleted(string id)
        {
            EnsureInitialized();
            return completed.Contains(id);
        }

        public List<string> GetDependentTilesToRun(string id)
        {
            EnsureInitialized();
            return dependedOnBy[id].Where(i => ShouldRun(i)).ToList();
        }

        public void AddInputToChunk(string name)
        {
            EnsureInitialized();
            inputsToChunk.Add(name);
        }

        /// <summary>
        /// </summary>
        /// <returns>returns true when all inputs have been chunked</returns>
        public bool InputChunked(string name)
        {
            EnsureInitialized();
            inputsToChunk.Remove(name);
            return inputsToChunk.Count == 0;
        }

        public bool ShouldRun(string id)
        {
            EnsureInitialized();
            return !AlreadyProcessed(id) && dependsOn[id].All(i => completed.Contains(i));
        }
    }
}
