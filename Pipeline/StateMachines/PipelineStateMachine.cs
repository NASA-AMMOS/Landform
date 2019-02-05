using System;
using System.Linq;
using System.Collections.Generic;
using log4net;
using OPS.Util;
using OPS.Cloud;
using OPS.Geometry;
using OPS.Pipeline.TileServer;

namespace OPS.Pipeline
{
    //https://github.jpl.nasa.gov/ProtoSpace/ps-pipeline/issues/159
    //TODO this needs to get refactored to be a generic base class for all Landform workflows, not just tiling
    public abstract class PipelineStateMachine
    {
        public enum ProjectType { GenericTiling, MSL };

        public static Dictionary<ProjectType, Type> StateMachines = new Dictionary<ProjectType, Type>()
        {
            { ProjectType.GenericTiling, typeof(GenericTilingStateMachine) },
            { ProjectType.MSL, typeof(MSLStateMachine) },
        };

        protected CloudPipeline pipeline;
        protected ProjectCache projectCache;
        protected string projectName;
        protected TypeDispatcher dispatcher;

        protected void LogInfo(string msg, params Object[] args)
        {
            pipeline.LogInfo("[{0}] ({1}) {2}", projectName, GetType().Name, string.Format(msg, args));
        }

        protected void LogWarn(string msg, params Object[] args)
        {
            pipeline.LogWarn("[{0}] ({1}) {2}", projectName, GetType().Name, string.Format(msg, args));
        }

        protected void LogError(string msg, params Object[] args)
        {
            pipeline.LogError("[{0}] ({1}) {2}", projectName, GetType().Name, string.Format(msg, args));
        }

        public PipelineStateMachine(CloudPipeline pipeline, string projectName)
        {
            this.pipeline = pipeline;
            this.projectName = projectName;
            projectCache = new ProjectCache(pipeline, projectName, pipeline.Logger);
            InitDispatcher();
        }

        virtual protected TypeDispatcher InitDispatcher()
        {
            dispatcher = new TypeDispatcher()
                .Case((CreateProjectMessage m) => CreateProject(m))
                .Case((DeleteProjectMessage m) => DeleteProject())
                .Case((AddInputMessage m) => AddInput(m))
                .Case((RunProjectMessage m) => RunProject())
                .Case((DefineTilesMessage m) => TilesDefined())
                .Case((ChunkInputMessage m) => InputChunked(m.InputName))
                .Case((TileCompletedMessage m) => TileCompleted(m.TileId))
                .Case((BuildTilesetJsonMessage m) => TilesetCompleted());
            dispatcher.Unhandled = (t, x) => pipeline.LogError("unknown message type: " + t);
            return dispatcher;
        }

        virtual public void ProcessMessage(QueueMessage m)
        {
            if (m.ProjectName != projectName)
            {
                throw new ArgumentException(string.Format("received message for project \"{0}\", expected \"{1}\"",
                                                          m.ProjectName, projectName));
            }
            dispatcher.Handle(m);
        }
        
        virtual protected void CreateProject(CreateProjectMessage m)
        {
            var project = TilingProject.Find(pipeline, projectName);
            if (project == null)
            {
                LogInfo("creating project");
                TilingProject.Create(pipeline, projectName, m.TilingScheme, m.SkirtMode, m.ReconMethod,
                                     m.FacesPerTile, m.TileResolution, m.ProjectType,
                                     m.ExportMeshFormat, m.ExportImageFormat);
            }
            else
            {
                //could get here if the project was created after the check in CreateProject.cs
                LogError("cannot create project, already exists");
            }
        }

        virtual protected void DeleteProject()
        {
            var project = TilingProject.Find(pipeline, projectName);
            if (project != null)
            {
                if (!project.StartedRunning || project.FinishedRunning)
                {
                    LogInfo("deleting project");
                    project.Delete(pipeline, ignoreErrors: true); //can take a little while
                    LogInfo("project deleted");
                }
                else
                {
                    //could get here if the project was run after the check in DeleteProject.cs
                    LogError("cannot delete project, currently running");
                }
            }
            else
            {
                //could get here if the project was deleted after the check in DeleteProject.cs
                LogError("cannot delete project, project not found");
            }
        }

        virtual protected void AddInput(AddInputMessage m)
        {
            var project = TilingProject.Find(pipeline, projectName);
            if (project != null)
            {
                if (!project.StartedRunning)
                {
                    //it's not an error to upload an input with the same name again - the last upload wins
                    LogInfo("adding/updating input " + m.Name);
                    TilingInput.Create(pipeline, m.Name, project, m.MeshUrl, m.ImageUrl, m.TileId);
                }
                else
                {
                    //could get here if the project was run after the check in UploadInput.cs
                    LogError("cannot add/update input, already run");
                }
                
            }
            else
            {
                //could get here if the project was deleted after the check in UploadInput.cs
                LogError("cannot add input, project not found");
            }
        }

        virtual protected void RunProject()
        {
            LogInfo("defining tiles");
            RunProject(new DefineTilesMessage(projectName));
        }

        virtual protected void RunProject(QueueMessage nextMessage)
        {
            projectCache.Reset();
            var project = TilingProject.Find(pipeline, projectName);
            if (project != null)
            {
                LogInfo("running project");
                project.StartedRunning = true;
                project.Save(pipeline);
                pipeline.WorkerQueue.Enqueue(nextMessage);
            }
            else
            {
                //could get here if the project was deleted after the check in RunProject.cs
                LogError("cannot run project, project not found");
            }
        }

        virtual protected void TilesDefined()
        {
            LogInfo("tiles defined");
            var project = TilingProject.Find(pipeline, projectName);
            if (SkipChunking(project))
            {
                LogInfo("input chunking skipped");
                BuildNodes(project);
            }
            else
            {
                bool allChunked = ChunkInputs(project);
                if (allChunked)
                {
                    LogInfo("all inputs chunked");
                    BuildNodes(project);
                }
            }
        }

        virtual protected bool SkipChunking(TilingProject project)
        {
            return false;
        }

        /// <summary>
        /// </summary>
        /// <returns>true iff all inputs have already been chunked</returns>
        virtual protected bool ChunkInputs(TilingProject project)
        {
            bool allChunked = true;
            foreach (var inputName in project.InputNames)
            {
                var input = TilingInput.Find(pipeline, projectName, inputName);
                if (!input.Chunked)
                {
                    allChunked = false;
                    LogInfo("chunking input " + inputName);
                    projectCache.AddInputToChunk(inputName);
                    pipeline.WorkerQueue.Enqueue(new ChunkInputMessage(projectName) { InputName = inputName });
                }
                else
                {
                    LogInfo("input " + inputName + " already chunked");
                }
            }
            return allChunked;
        }

        virtual protected void InputChunked(string inputName)
        {
            LogInfo("input " + inputName + " chunked");
            bool allChunked = projectCache.InputChunked(inputName);
            if (allChunked)
            {
                LogInfo("all inputs chunked");
                var project = TilingProject.Find(pipeline, projectName);
                BuildNodes(project);
            }
        }

        abstract protected QueueMessage MakeLeafJobMessage(List<string> leaves);

        virtual protected void BuildNodes(TilingProject project)
        {
            SceneNode root = TilingNode.BuildTreeFromDatabase(pipeline, project);

            List<List<SceneNode>> leafGroups = new List<List<SceneNode>>();
            CollectLeafGroups(root, leafGroups);
            int totalLeaves = 0, leafJobs = 0, unprocessedLeaves = 0;
            foreach (var group in leafGroups)
            {
                totalLeaves += group.Count;
                var names = group.Select(n => n.Name).Where(n => !projectCache.AlreadyProcessed(n)).ToList();
                if (names.Count > 0)
                {
                    leafJobs++;
                    pipeline.WorkerQueue.Enqueue(MakeLeafJobMessage(names));
                    foreach (var name in names)
                    {
                        unprocessedLeaves++;
                        projectCache.MarkEnqueued(name);
                    }
                }
            }
            LogInfo("building " + unprocessedLeaves + " uprocessed leaves" +
                    " (" + leafJobs + " jobs, " + totalLeaves + " total leaves)");

            var parents = root.NonLeaves();
            int totalParents = 0, readyParents = 0;
            foreach (var parent in parents)
            {
                totalParents++;
                string name = parent.Name;
                if (projectCache.ShouldRun(name))
                {
                    readyParents++;
                    pipeline.WorkerQueue.Enqueue(new BuildParentMessage(projectName) { TileId = name});
                    projectCache.MarkEnqueued(name);
                }
            }
            LogInfo("building " + readyParents + " unprocessed but ready parents (" + totalParents + " total parents)");

            if (projectCache.AlreadyCompleted(root.Name))
            {
                RootCompleted();
            }
        }       

        /// <summary>
        /// collect all leaves in groups up to the given max size per group
        /// attempts to group leaves which are spatially close together into the same group
        /// uses tree topology as a proxy for spatial proximity
        /// </summary>
        virtual protected Queue<SceneNode> CollectLeafGroups(SceneNode node, List<List<SceneNode>> groups,
                                                             int maxGroupSize = 32)
        {
            var result = new Queue<SceneNode>();
            if (node.IsLeaf)
            {
                result.Enqueue(node);
                return result;
            }
            foreach (var c in node.Children)
            {
                var tmp = CollectLeafGroups(c, groups, maxGroupSize);
                foreach (var e in tmp)
                {
                    result.Enqueue(e);
                }
            }
            while (result.Count > maxGroupSize)
            {
                List<SceneNode> group = new List<SceneNode>();
                for (int i = 0; i < maxGroupSize; i++)
                {
                    group.Add(result.Dequeue());
                }
                groups.Add(group);
            }
            if (node.Parent == null && result.Count != 0)
            {
                groups.Add(result.ToList());
                result.Clear();
            }
            return result;
        }

        virtual protected void TileCompleted(string tileId)
        {
            projectCache.MarkDone(tileId);
            if (tileId == projectCache.RootId())
            {
                RootCompleted();
            }
            else
            {
                int n = 0;
                foreach (var pid in projectCache.GetDependentTilesToRun(tileId))
                {
                    n++;
                    LogInfo("building parent " + pid);
                    pipeline.WorkerQueue.Enqueue(new BuildParentMessage(projectName) { TileId = pid });
                    projectCache.MarkEnqueued(pid);
                }
                LogInfo("tile " + tileId + " completed, enqueued " + n + " parents");
            }
        }

        virtual protected void RootCompleted()
        {
            LogInfo("root tile completed, building tileset JSON");
            pipeline.WorkerQueue.Enqueue(new BuildTilesetJsonMessage(projectName));
        }

        virtual protected void TilesetCompleted()
        {
            var project = TilingProject.Find(pipeline, projectName);
            project.FinishedRunning = true;
            project.Save(pipeline);
            LogInfo("finished running");
            projectCache.Reset();
            pipeline.CleanupTempDir();
        }
    }

    public class CreateProjectMessage : QueueMessage
    {
        public TilingScheme TilingScheme;
        public SkirtMode SkirtMode;
        public MeshReconMethod ReconMethod;
        public int FacesPerTile;
        public int TileResolution;
        public string ProjectType;
        public string ExportMeshFormat;
        public string ExportImageFormat;
        public CreateProjectMessage() { }
        public CreateProjectMessage(string projectName) : base(projectName) { }
    }

    public class DeleteProjectMessage : QueueMessage
    {
        public DeleteProjectMessage() { }
        public DeleteProjectMessage(string projectName) : base(projectName) { }
    }

    public class AddInputMessage : QueueMessage
    {
        public string Name;
        public string MeshUrl;
        public string ImageUrl;
        public string TileId;
        public AddInputMessage() { }
        public AddInputMessage(string projectName) : base(projectName) { }
    }

    public class RunProjectMessage : QueueMessage
    {
        public RunProjectMessage() { }
        public RunProjectMessage(string projectName) : base(projectName) { }
    }

    public class TileCompletedMessage : QueueMessage
    {
        public string TileId;
        public TileCompletedMessage(string projectName) : base(projectName) { }
    }
}
