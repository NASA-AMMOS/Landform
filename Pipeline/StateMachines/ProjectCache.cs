using OPS.Plumbing;
using System.Collections.Generic;
using System.Linq;

namespace OPS.Pipeline.TileServer
{
    class ProjectCache
    {
        HashSet<string> ids;
        Dictionary<string, List<string>> dependedOnBy;
        Dictionary<string, List<string>> dependsOn;
        HashSet<string> completed;
        HashSet<string> enqued;
        public string RootId { get; private set; }

        PipelineCore pipeline;
        TilingProject project;
        object lockObj = new object();

        public ProjectCache(PipelineCore pipeline, string projectName)
        {
            this.pipeline = pipeline;
            this.project = TilingProject.Find(pipeline.DynamoContext, projectName);
            Init();
        }

        public void Refresh()
        {
            Init();
        }

        void Init()
        {
            lock (lockObj)
            {
                ids = new HashSet<string>();
                dependedOnBy = new Dictionary<string, List<string>>();
                dependsOn = new Dictionary<string, List<string>>();
                completed = new HashSet<string>();
                enqued = new HashSet<string>();
                var list = TilingNode.Find(pipeline.DynamoContext, project.Name).ToList();
                foreach (var n in list)
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
                        RootId = n.Id;
                    }
                }
            }
        }

        public void MarkEnqued(string id)
        {
            lock (lockObj)
            {
                enqued.Add(id);
            }
        }

        public void MarkDone(string id)
        {
            lock (lockObj)
            {
                completed.Add(id);
            }
        }

        public bool AlreadyProcessed(string id)
        {
            lock (lockObj)
            {
                return enqued.Contains(id) || completed.Contains(id);
            }
        }

        public bool ShouldRun(string id)
        {
            lock (lockObj)
            {
                // Don't run if we node is done or enqueued
                if (AlreadyProcessed(id))
                {
                    return false;
                }
                // Only run if all nodes id depends on are completed
                return dependsOn[id].All(i => completed.Contains(i));
            }
        }

        public List<string> GetDependentTilesToRun(string id)
        {
            lock (lockObj)
            {
                // Find all nodes that depend on id and return only those that are ready to run
                return dependedOnBy[id].Where(i => ShouldRun(i)).ToList();
            }
        }

        public List<string> GetTilesReadyToRun()
        {
            lock (lockObj)
            {
                var ready = ids.Where(i => ShouldRun(i)).ToList();
                return ready;
            }
        }
    }
}
