using System;
using System.Linq;
using System.Collections.Generic;
using JPLOPS.Util;
using JPLOPS.Geometry;
using JPLOPS.Pipeline.TilingServer;

namespace JPLOPS.Pipeline
{
    public enum ProjectType { GenericTiling, ParentTiling };

    public class CreateProjectMessage : PipelineMessage
    {
        public ProjectType ProjectType;
        public string ProductPath;

        public TilingScheme TilingScheme;
        public int MaxFacesPerTile;
        public double MinTileExtent;
        public double MaxLeafArea;
        public MeshReconstructionMethod ParentReconstructionMethod;
        public SkirtMode SkirtMode;

        public TextureMode TextureMode;
        public int MaxTextureResolution;
        public double MaxTexelsPerMeter;
        public double MaxTextureStretch;
        public bool PowerOfTwoTextures;
        public bool ConvertLinearRGBToSRGB;

        public bool EmbedIndexImages;

        public string ExportMeshFormat;
        public string ExportImageFormat;

        public CreateProjectMessage() { }
        public CreateProjectMessage(string projectName) : base(projectName) { }
    }

    public class DeleteProjectMessage : PipelineMessage
    {
        public DeleteProjectMessage() { }
        public DeleteProjectMessage(string projectName) : base(projectName) { }
    }

    public class AddInputMessage : PipelineMessage
    {
        public string Name;
        public string MeshUrl;
        public string ImageUrl;
        public string IndexUrl;
        public string TileId;
        public AddInputMessage() { }
        public AddInputMessage(string projectName) : base(projectName) { }

        public override string Info()
        {
            return string.Format("[{0}] AddInput input {1} tile {2}", ProjectName, Name, TileId);
        }
    }

    public class RunProjectMessage : PipelineMessage
    {
        public RunProjectMessage() { }
        public RunProjectMessage(string projectName) : base(projectName) { }
    }

    public class TileCompletedMessage : PipelineMessage
    {
        public string TileId;
        public TileCompletedMessage(string projectName) : base(projectName) { }

        public override string Info()
        {
            return string.Format("[{0}] TileCompleted tile {1}", ProjectName, TileId);
        }
    }

    public class StatusMessage : PipelineMessage
    {
        public string Operation;
        public string TaskId;
        public string Status;
        public bool Done;
        public bool Error;
        public StatusMessage(string projectName, string taskId, string operation, string status, bool done = false,
                             bool error = false)
            : base(projectName)
        {
            this.TaskId = taskId;
            this.Operation = operation;
            this.Status = status;
            this.Done = done;
            this.Error = error;
        }
    }

    //TODO this needs to get refactored to be a generic base class for all Landform workflows, not just tiling
    public abstract class PipelineStateMachine : ILogger
    {
        public static bool LessSpew;
        public static bool SingleWorkflowSpew;

        public const double DEF_LONG_TASK_WARN_SEC = 5 * 60;

        public static Dictionary<ProjectType, Type> StateMachines = new Dictionary<ProjectType, Type>()
        {
            { ProjectType.GenericTiling, typeof(GenericTilingStateMachine) },
            { ProjectType.ParentTiling, typeof(ParentTilingStateMachine) },
        };

        public static PipelineStateMachine CreateInstance(PipelineCore pipeline, ProjectType projectType,
                                                          string projectName)
        {
            return (PipelineStateMachine)Activator.CreateInstance(StateMachines[projectType], pipeline, projectName);
        }

        public static ProjectType? GetProjectType(PipelineCore pipeline, PipelineMessage message)
        {
            if (message is CreateProjectMessage)
            {
                return ((CreateProjectMessage)message).ProjectType;
            }
            else
            {
                //TODO: TilingProject should be merged with Project
                TilingProject project = TilingProject.Find(pipeline, message.ProjectName);
                if (project != null)
                {
                    return project.ProjectType;
                }
                else
                {
                    return null;
                }
            }
        }

        protected PipelineCore pipeline;
        protected ProjectCache projectCache;
        protected string projectName;
        protected TypeDispatcher dispatcher;
        protected string logPrefix;

        protected class Status
        {
            public double StartSec;
            public string LatestOperation;
            public string LatestStatus;
            public Status(StatusMessage m)
            {
                StartSec = UTCTime.Now();
                LatestOperation = m.Operation;
                LatestStatus = m.Status;
            }
        }

        protected Dictionary<string, Status> status = new Dictionary<string, Status>();

        public void LogLess(string msg, params Object[] args)
        {
            if (LessSpew)
            {
                pipeline.LogVerbose(logPrefix + string.Format(msg, args));
            }
            else
            {
                pipeline.LogInfo(logPrefix + string.Format(msg, args));
            }
        }

        public void LogInfo(string msg, params Object[] args)
        {
            pipeline.LogInfo(logPrefix + string.Format(msg, args));
        }

        public void LogVerbose(string msg, params Object[] args)
        {
            pipeline.LogVerbose(logPrefix + string.Format(msg, args));
        }

        public void LogDebug(string msg, params Object[] args)
        {
            pipeline.LogDebug(logPrefix + string.Format(msg, args));
        }

        public void LogWarn(string msg, params Object[] args)
        {
            pipeline.LogWarn(logPrefix + string.Format(msg, args));
        }

        public void LogError(string msg, params Object[] args)
        {
            pipeline.LogError(logPrefix + string.Format(msg, args));
        }

        public void LogException(Exception ex, string msg = null, int maxAggregateSpew = 1, bool stackTrace = false)
        {
            msg = logPrefix + (msg ?? "");
            pipeline.LogException(ex, msg, maxAggregateSpew, stackTrace);
        }

        public PipelineStateMachine(PipelineCore pipeline, string projectName)
        {
            this.pipeline = pipeline;
            this.projectName = projectName;
            logPrefix = SingleWorkflowSpew ? "" : string.Format("[{0}] ({1}) ", projectName, GetType().Name);
            projectCache = new ProjectCache(pipeline, projectName, this);
            dispatcher = MakeDispatcher();
        }

        virtual protected TypeDispatcher MakeDispatcher()
        {
            var ret = new TypeDispatcher()
                .Case((CreateProjectMessage m) => CreateProject(m))
                .Case((DeleteProjectMessage m) => DeleteProject())
                .Case((AddInputMessage m) => AddInput(m))
                .Case((RunProjectMessage m) => RunProject())
                .Case((DefineTilesMessage m) => TilesDefined())
                .Case((ChunkInputMessage m) => InputChunked(m.InputName))
                .Case((TileCompletedMessage m) => TileCompleted(m.TileId))
                .Case((BuildTilesetJsonMessage m) => TilesetCompleted())
                .Case((StatusMessage m) => UpdateStatus(m));
            ret.Unhandled = (t, x) => pipeline.LogError("unknown master message type: {0}", t);
            return ret;
        }

        virtual public void ProcessMessage(PipelineMessage m)
        {
            if (m.ProjectName != projectName)
            {
                throw new ArgumentException(string.Format("received message for project \"{0}\", expected \"{1}\"",
                                                          m.ProjectName, projectName));
            }
            dispatcher.Handle(m);
        }

        virtual protected void UpdateStatus(StatusMessage m)
        {
            string id = m.TaskId;
            if (id != null)
            {
                if (!status.ContainsKey(id))
                {
                    status[id] = new Status(m);
                }
                else if (m.Done || m.Error)
                {
                    status.Remove(id);
                }
                else
                {
                    var s = status[id];
                    s.LatestOperation = m.Operation;
                    s.LatestStatus = m.Status;
                }
            }
            if (m.Error)
            {
                TilesetCompleted(m.Status);
            }
        }

        virtual public void SpewStatus(double warnSec = DEF_LONG_TASK_WARN_SEC, bool verbose = false)
        {
            LogInfo("{0} tasks in flight", status.Count);
            TilingNode.DumpLRUCacheStats(pipeline);
            int ndr = PathHelper.NumDeleteRetries;
            if (ndr > 0)
            {
                pipeline.LogWarn("{0} file delete retries", ndr);
            }
            var now = UTCTime.Now();
            foreach (var entry in status)
            {
                var id = entry.Key;
                var st = entry.Value;
                var sec = now - st.StartSec;
                var msg = string.Format("{0} {1}: {2}, running for {3}",
                                        st.LatestOperation, id, st.LatestStatus, Fmt.HMS(sec * 1e3));
                if (!SingleWorkflowSpew)
                {
                    msg = string.Format("[{0}] {1}", projectName, msg);
                }
                if (sec > warnSec)
                {
                    LogWarn(msg + " > {0}", Fmt.HMS(warnSec * 1e3));
                }
                else if (verbose)
                {
                    LogVerbose(msg);
                }
            }
        }

        virtual protected void CreateProject(CreateProjectMessage m)
        {
            var project = TilingProject.Find(pipeline, projectName);
            if (project == null)
            {
                LogInfo("creating project");
                project = TilingProject.Create(pipeline, projectName, m.ProjectType, m.ProductPath);

                project.TilingScheme = m.TilingScheme;
                project.MaxFacesPerTile = m.MaxFacesPerTile;
                project.MinTileExtent = m.MinTileExtent;
                project.MaxLeafArea = m.MaxLeafArea;
                project.ParentReconstructionMethod = m.ParentReconstructionMethod;
                project.SkirtMode = m.SkirtMode;

                project.TextureMode = m.TextureMode;
                project.MaxTextureResolution = m.MaxTextureResolution;
                project.MaxTexelsPerMeter = m.MaxTexelsPerMeter;
                project.MaxTextureStretch = m.MaxTextureStretch;
                project.PowerOfTwoTextures = m.PowerOfTwoTextures;
                project.ConvertLinearRGBToSRGB = m.ConvertLinearRGBToSRGB;

                project.EmbedIndexImages = m.EmbedIndexImages;

                project.ExportMeshFormat = m.ExportMeshFormat;
                project.ExportImageFormat = m.ExportImageFormat;

                pipeline.SaveDatabaseItem(project);
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
                    LogLess("adding/updating input {0}", m.Name);
                    var input =
                        TilingInput.Create(pipeline, m.Name, project, m.MeshUrl, m.ImageUrl, m.IndexUrl, m.TileId);
                    var inputs = project.LoadInputNames(pipeline);
                    if (!inputs.Contains(input.Name))
                    {
                        inputs.Add(input.Name);
                        project.SaveInputNames(inputs, pipeline);
                        pipeline.SaveDatabaseItem(project);
                    }
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
            LogInfo("running project");
            RunProject(() => pipeline.EnqueueToWorkers(new DefineTilesMessage(projectName)));
        }

        virtual protected void RunProject(Action next)
        {
            projectCache.Reset();
            var project = TilingProject.Find(pipeline, projectName);
            if (project != null)
            {
                project.StartedRunning = true;
                project.Save(pipeline);
                next();
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
            if (TilingSchemeBase.IsUserProvided(project.TilingScheme))
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

        /// <summary>
        /// </summary>
        /// <returns>true iff all inputs have already been chunked</returns>
        virtual protected bool ChunkInputs(TilingProject project)
        {
            bool allChunked = true;
            foreach (var inputName in project.LoadInputNames(pipeline))
            {
                var input = TilingInput.Find(pipeline, projectName, inputName);
                if (input == null)
                {
                    throw new Exception("tiling input not found: " + inputName);
                }
                if (!input.Chunked)
                {
                    allChunked = false;
                    LogLess("chunking input {0}", inputName);
                    projectCache.AddInputToChunk(inputName);
                    pipeline.EnqueueToWorkers(new ChunkInputMessage(projectName) { InputName = inputName });
                }
                else
                {
                    LogLess("input {0} already chunked", inputName);
                }
            }
            return allChunked;
        }

        virtual protected void InputChunked(string inputName)
        {
            LogLess("input {0} chunked", inputName);
            bool allChunked = projectCache.InputChunked(inputName);
            if (allChunked)
            {
                LogInfo("all inputs chunked");
                BuildNodes(TilingProject.Find(pipeline, projectName));
            }
        }

        abstract protected PipelineMessage MakeLeafJobMessage(List<string> leaves);

        virtual protected void BuildNodes(TilingProject project)
        {
            LogInfo("building tile tree from database");
            SceneNode root = TilingNode.BuildTreeFromDatabase(pipeline, project);
            BuildLeaves(project, root);
            BuildParents(project, root);
        }

        virtual protected void BuildLeaves(TilingProject project, SceneNode root = null)
        {
            if (root == null)
            {
                LogInfo("building tile tree from database");
                root = TilingNode.BuildTreeFromDatabase(pipeline, project);
            }

            LogInfo("enqueueing leaf jobs");
            List<List<SceneNode>> leafGroups = new List<List<SceneNode>>();
            CollectLeafGroupsByChunkDependency(root, leafGroups, TilingDefaults.MAX_LEAF_GROUP);
            int totalLeaves = 0, leafJobs = 0, toGo = 0;
            foreach (var group in leafGroups)
            {
                totalLeaves += group.Count;
                var names = group.Select(n => n.Name).Where(n => !projectCache.AlreadyProcessed(n)).ToList();
                if (names.Count > 0)
                {
                    leafJobs++;
                    toGo += names.Count;
                    names.ForEach(name => projectCache.MarkEnqueued(name));
                    pipeline.EnqueueToWorkers(MakeLeafJobMessage(names));
                }
            }

            LogInfo("building {0} uprocessed leaves ({1} jobs, {2} total leaves)", toGo, leafJobs, totalLeaves);
        }

        protected void BuildParents()
        {
            BuildParents(TilingProject.Find(pipeline, projectName));
        }
            
        virtual protected void BuildParents(TilingProject project, SceneNode root = null)
        {
            if (root == null)
            {
                LogInfo("build parents: building tile tree from database");
                root = TilingNode.BuildTreeFromDatabase(pipeline, project);
            }

            LogInfo("enqueueing parent jobs");
            var parents = root.NonLeaves();
            int totalParents = 0, readyParents = 0;
            foreach (var parent in parents)
            {
                totalParents++;
                string name = parent.Name;
                if (projectCache.ShouldRun(name))
                {
                    readyParents++;
                    projectCache.MarkEnqueued(name);
                    pipeline.EnqueueToWorkers(new BuildParentMessage(projectName) { TileId = name}); 
                }
            }
            LogInfo("building {0} unprocessed but ready parents ({1} total parents)", readyParents, totalParents);

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
                                                             int maxGroupSize)
        {
            if (maxGroupSize < 1)
            {
                throw new ArgumentException("maxGroupSize < 1: " + maxGroupSize);
            }
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

        /// <summary>
        /// collects leaves into groups based on the chunks that they depend on. All leaves in any given group will
        /// depend on the exact same set of chunks. This should increase performance of BuilbakedLeaves.process since
        /// the minimum number of chunks will be loaded when performing a mesh clip to define the meshes for individual
        /// leaves
        /// </summary>
        /// <param name="root">The root node of the tree whose leaves should be grouped</param>
        /// <param name="groups">the output list of scene node groups 'collected' by this function</param>
        /// <param name="maxGroupSize">the maximum size of any given group</param>
        virtual protected void CollectLeafGroupsByChunkDependency(SceneNode root, List<List<SceneNode>> groups,
                                                                  int maxGroupSize)
        {
            // make sure group size is positive
            if (maxGroupSize < 1)
            {
                throw new ArgumentException("maxGroupSize < 1: " + maxGroupSize);
            }

            // TODO: consider looking at intersection of parent tile, only checking intersection of children on subset
            // of chunks that intersect the parent

            var project = TilingProject.Find(pipeline, projectName);

            var chunks = new List<TilingInputChunk>();
            foreach (var inputName in project.LoadInputNames(pipeline))
            {
                var input = TilingInput.Find(pipeline, projectName, inputName);
                if (input == null)
                {
                    throw new Exception("tiling input not found: " + inputName);
                }
                IEnumerable<string> chunkIds = null;
                lock (input.ChunkIds)
                {
                    chunkIds = input.ChunkIds.ToArray();
                }
                foreach (var chunkId in chunkIds)
                {
                    TilingInputChunk chunk = TilingInputChunk.Find(pipeline, chunkId);
                    chunks.Add(chunk);
                }
            }

            var leavesGroupedByChunk = new Dictionary<string, List<SceneNode>>();

            // group leaves by 'chunk group key', where a 'chunk group key' is the concatenation of all
            //   IDs of chunks that intersect the leaf. Should be unique per set of chunks since order of the
            //   'chunks' array remains consistent througout tree traversal
            var dfsTraversalStack = new Stack<SceneNode>();
            dfsTraversalStack.Push(root);
            while(dfsTraversalStack.Count > 0)
            {
                var node = dfsTraversalStack.Pop();

                // if we've come across a node leaf, put it in the appropriate group
                if (node.IsLeaf)
                {
                    // compute a 'chunk group key' for this leaf
                    var leafTile = TilingNode.Find(pipeline, projectName, node.Name);
                    var chunkGroupKey = string.Empty;
                    foreach (var chunk in chunks)
                    {
                        if (leafTile.GetBoundsChecked().Intersects(chunk.GetBounds()))
                        {
                            chunkGroupKey += chunk.Id;
                        }
                    }

                    // add this leaf to the dictionary list with corresponding 'chunk group key'
                    if (!leavesGroupedByChunk.ContainsKey(chunkGroupKey))
                    {
                        leavesGroupedByChunk.Add(chunkGroupKey, new List<SceneNode>());
                    }
                    leavesGroupedByChunk[chunkGroupKey].Add(node);
                }
                // otherwise... (is NOT a leaf, so continue depth first search for children)
                else
                {
                    foreach (var child in node.Children)
                    {
                        dfsTraversalStack.Push(child);
                    }
                }
            }

            // create leaf node groups based on chunk intersection grouping
            foreach (var chunkGroupKeyValuePair in leavesGroupedByChunk)
            {
                var group = new List<SceneNode>(maxGroupSize);

                // break each dictionary list into groups of maximum size 'maxGroupSize'
                foreach (var leafNode in chunkGroupKeyValuePair.Value)
                {
                    if (group.Count < maxGroupSize)
                    {
                        group.Add(leafNode);
                    }
                    else
                    {
                        groups.Add(group);
                        group = new List<SceneNode>(maxGroupSize);
                        group.Add(leafNode);
                    }
                }
                if(group.Count > 0)
                {
                    groups.Add(group);
                }
            }
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
                int numEnqueued = 0;
                foreach (var pid in projectCache.GetDependentTilesToRun(tileId))
                {
                    numEnqueued++;
                    LogLess("building parent {0}", pid);
                    projectCache.MarkEnqueued(pid);
                    pipeline.EnqueueToWorkers(new BuildParentMessage(projectName) { TileId = pid });
                }
                int nc = projectCache.NumCompleted, nn = projectCache.NumNodes;
                LogInfo("{0}/{1} nodes done ({2:F2}%): tile {3} completed, enqueued {4} parents",
                        nc, nn, 100 * nc / (float)nn, tileId, numEnqueued);
            }
        }

        virtual protected void RootCompleted()
        {
            LogInfo("root tile completed, building tileset JSON");
            pipeline.EnqueueToWorkers(new BuildTilesetJsonMessage(projectName));
        }

        virtual protected void TilesetCompleted(string error = null)
        {
            var project = TilingProject.Find(pipeline, projectName);
            project.FinishedRunning = true;
            project.ExecutionError = error;
            project.Save(pipeline);
            LogInfo("finished running" + (!string.IsNullOrEmpty(error) ? (" with " + error) : ""));
            projectCache.Reset();
            pipeline.CleanupTempDir();
        }
    }
}
