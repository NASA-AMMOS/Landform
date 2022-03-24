using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using JPLOPS.Util;
using JPLOPS.Pipeline.TilingServer;

namespace JPLOPS.Pipeline
{
    public class ProjectCache
    {
        PipelineCore pipeline;
        private string projectName;
        private ILogger logger;

        private bool initialized;

        private string rootId;
        private HashSet<string> ids;
        private Dictionary<string, IEnumerable<string>> dependedOnBy;
        private Dictionary<string, IEnumerable<string>> dependsOn;
        private HashSet<string> completed;
        private HashSet<string> enqueued;
        private HashSet<string> inputsToChunk;

        private int numNodes;
        public int NumNodes
        {
            get
            {
                return numNodes;
            }
        }

        public int NumCompleted
        {
            get
            {
                return completed.Count;
            }
        }

        public ProjectCache(PipelineCore pipeline, string projectName, ILogger logger)
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
            dependedOnBy = new Dictionary<string, IEnumerable<string>>();
            dependsOn = new Dictionary<string, IEnumerable<string>>();
            completed = new HashSet<string>();
            enqueued = new HashSet<string>();
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
                logger.LogInfo("initializing project cache");
            }

            Stopwatch sw = new Stopwatch();
            sw.Start();

            var project = TilingProject.Find(pipeline, projectName);
            if (project == null || !project.TilesDefined)
            {
                throw new System.Exception("cannot initialize cache for " + projectName +
                                           ": project not found or tiles not defined yet");
            }

            numNodes = 0;
            var nodes = TilingNode.Find(pipeline, project).ToList();
            foreach (var n in nodes)
            {
                ids.Add(n.Id);

                dependedOnBy.Add(n.Id, n.GetDependedOnBy());

                dependsOn.Add(n.Id, n.GetDependsOn());

                if (n.MeshUrl != null && n.GeometricError.HasValue)
                {
                    completed.Add(n.Id);
                }

                if (n.ParentId == null)
                {
                    rootId = n.Id;
                }

                numNodes++;
            }

            if (logger != null)
            {
                logger.LogInfo("initialized project cache in {0:F3}s, {1} nodes already completed",
                               0.001 * sw.ElapsedMilliseconds, completed.Count);
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
            enqueued.Add(id);
        }

        public void MarkDone(string id)
        {
            EnsureInitialized();
            completed.Add(id);
        }

        public bool AlreadyProcessed(string id)
        {
            EnsureInitialized();
            return enqueued.Contains(id) || completed.Contains(id);
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
