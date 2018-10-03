using System;
using System.Linq;
using System.Collections.Generic;
using OPS.Plumbing;
using OPS.Geometry;
using OPS.Pipeline.TileServer;
using log4net;

namespace OPS.Pipeline.TileServer
{
    abstract class PipelineStateMachine
    {
        private static ILog logger = LogManager.GetLogger(typeof(PipelineStateMachine));

        protected PipelineCore pipeline;
        protected TilingQueue workerQueue;
        protected ProjectCache projectCache;
        protected string projectName;

        public PipelineStateMachine(PipelineCore pipeline, TilingQueue workerQueue, string projectName)
        {
            this.pipeline = pipeline;
            this.workerQueue = workerQueue;
            this.projectName = projectName;
            this.projectCache = new ProjectCache();
        }

        //returns true iff the message was handled
        virtual public bool ProcessMessage(TilingQueueMessage m)
        {
            if (m.ProjectName != projectName)
            {
                throw new ArgumentException(string.Format("received message for project \"{0}\", expected \"{1}\"",
                                                          m.ProjectName, projectName));
            }

            bool processed = false;

            if (m.GetType() == typeof(CreateProjectMessage))
            {
                CreateProject((CreateProjectMessage)m);
                processed = true;
            }
            else if (m.GetType() == typeof(DeleteProjectMessage))
            {
                DeleteProject();
                processed = true;
            }
            else if (m.GetType() == typeof(AddInputMessage))
            {
                AddInput((AddInputMessage)m);
                processed = true;
            }
            //RunProjectMessage is handled differently by subclasses
            else if (m.GetType() == typeof(DefineTilesMessage))
            {
                TilesDefined();
                processed = true;
            }
            else if (m.GetType() == typeof(ChunkInputMessage))
            {
                InputChunked(((ChunkInputMessage)m).InputName);
                processed = true;
            }
            else if (m.GetType() == typeof(TileCompletedMessage))
            {
                TileCompleted(((TileCompletedMessage)m).TileId);
                processed = true;
            }
            else if (m.GetType() == typeof(BuildTilesetJsonMessage))
            {
                TilesetCompleted();
                processed = true;
            }

            return processed;
        }
        
        virtual protected void CreateProject(CreateProjectMessage m)
        {
            var project = TilingProject.Find(pipeline.DynamoContext, projectName);
            if (project == null)
            {
                logger.Info("creating project " + projectName);
                TilingProject.Create(pipeline.DynamoContext, projectName, m.TilingScheme, m.SkirtMode, m.ReconMethod,
                                     m.FacesPerTile, m.TileResolution, m.ProjectType);
            }
            else
            {
                //could get here if the project was created after the check in CreateProject.cs
                logger.Error("cannot create project " + projectName + ": project already exists");
            }
        }

        virtual protected void DeleteProject()
        {
            var project = TilingProject.Find(pipeline.DynamoContext, projectName);
            if (project != null)
            {
                if (!project.StartedRunning || project.FinishedRunning)
                {
                    logger.Info("deleting project " + projectName);
                    project.Delete(pipeline, true /* ignoreErrors */, logger); //can take a little while
                    logger.Info("project " + projectName + " deleted");
                }
                else
                {
                    //could get here if the project was run after the check in DeleteProject.cs
                    logger.Error("cannot delete project " + projectName + ": currently running");
                }
            }
            else
            {
                //could get here if the project was deleted after the check in DeleteProject.cs
                logger.Error("cannot delete project " + projectName + ": project not found");
            }
        }

        virtual protected void AddInput(AddInputMessage m)
        {
            var project = TilingProject.Find(pipeline.DynamoContext, projectName);
            if (project != null)
            {
                if (!project.StartedRunning)
                {
                    //it's not an error to upload an input with the same name again - the last upload wins
                    logger.Info("adding/updating input " + m.Name + " in project " + projectName);
                    TilingInput.Create(pipeline.DynamoContext, m.Name, project, m.MeshUrl, m.ImageUrl, m.TileId);
                }
                else
                {
                    //could get here if the project was run after the check in UploadInput.cs
                    logger.Error("cannot add/update input in project " + projectName + ": already run");
                }
                
            }
            else
            {
                //could get here if the project was deleted after the check in UploadInput.cs
                logger.Error("cannot add input to project " + projectName + ": project not found");
            }
        }

        virtual protected void RunProject(TilingQueueMessage nextMessage)
        {
            var project = TilingProject.Find(pipeline.DynamoContext, projectName);
            if (project != null)
            {
                logger.Info("running project " + projectName);
                project.StartedRunning = true;
                project.Save(pipeline.DynamoContext);
                projectCache.Clear();
                workerQueue.Enqueue(nextMessage);
            }
            else
            {
                //could get here if the project was deleted after the check in RunProject.cs
                logger.Error("cannot run project " + projectName + ": project not found");
            }
        }

        virtual protected void TilesDefined()
        {
            logger.Info("tiles defined in " + projectName);
            var project = TilingProject.Find(pipeline.DynamoContext, projectName);
            projectCache.Init(pipeline.DynamoContext, project); //must be called after tiles are defined
            if (SkipChunking(project))
            {
                BuildLeaves(project);
            }
            else
            {
                bool allChunked = ChunkInputs(project);
                if (allChunked)
                {
                    BuildLeaves(project);
                }
            }
        }

        virtual protected bool SkipChunking(TilingProject project)
        {
            return false;
        }

        //returns true if all inputs have already been chunked
        virtual protected bool ChunkInputs(TilingProject project)
        {
            bool allChunked = true;
            foreach (var inputName in project.InputNames)
            {
                var input = TilingInput.Find(pipeline.DynamoContext, projectName, inputName);
                if (!input.Chunked)
                {
                    allChunked = false;
                    logger.Info("chunking input " + inputName + " in " + projectName);
                    projectCache.AddInputToChunk(inputName);
                    workerQueue.Enqueue(new ChunkInputMessage(projectName, inputName));
                }
                else
                {
                    logger.Info("input " + inputName + " in " + projectName + " already chunked");
                }
            }
            return allChunked;
        }

        virtual protected void InputChunked(string inputName)
        {
            logger.Info("input " + inputName + " chunked in " + projectName);
            bool allChunked = projectCache.InputChunked(inputName);
            if (allChunked)
            {
                logger.Info("all inputs chunked in " + projectName);
                var project = TilingProject.Find(pipeline.DynamoContext, projectName);
                BuildLeaves(project);
            }
        }

        abstract protected void BuildLeaves(TilingProject project);

        virtual protected void TileCompleted(string tileId)
        {
            logger.Info("tile " + tileId + " completed in " + projectName);
            
            projectCache.MarkDone(tileId);
            if (tileId == projectCache.RootId)
            {
                logger.Info("building tileset JSON in " + projectName);
                workerQueue.Enqueue(new BuildTilesetJsonMessage(projectName));
            }
            else
            {
                int n = 0;
                foreach (var pid in projectCache.GetDependentTilesToRun(tileId))
                {
                    n++;
                    logger.Info("enquing parent " + pid + " in " + projectName);
                    workerQueue.Enqueue(new BuildParentMessage(projectName, pid));
                    projectCache.MarkEnqueued(pid);
                }
                logger.Info("enqueued " + n + " parents in " + projectName);
            }
        }

        virtual protected void TilesetCompleted()
        {
            var project = TilingProject.Find(pipeline.DynamoContext, projectName);
            project.FinishedRunning = true;
            project.Save(pipeline.DynamoContext);
            logger.Info(projectName + " finished running");
            projectCache.Clear();
        }

        virtual protected Queue<SceneNode> GroupSceneNodesIntoJobs(SceneNode node, List<List<SceneNode>> outputGroups,
                                                                   int nodesPerGroup = 32)
        {
            var result = new Queue<SceneNode>();
            if (node.IsLeaf)
            {
                result.Enqueue(node);
                return result;
            }
            foreach (var c in node.Children)
            {
                var tmp = GroupSceneNodesIntoJobs(c, outputGroups, nodesPerGroup);
                foreach (var e in tmp)
                {
                    result.Enqueue(e);
                }
            }
            while (result.Count > nodesPerGroup)
            {
                List<SceneNode> outputGroup = new List<SceneNode>();
                for (int i = 0; i < nodesPerGroup; i++)
                {
                    outputGroup.Add(result.Dequeue());
                }
                outputGroups.Add(outputGroup);
            }
            if (node.Parent == null && result.Count != 0)
            {
                outputGroups.Add(result.ToList());
                result.Clear();
            }
            return result;
        }

    }

    public class CreateProjectMessage : TilingQueueMessage
    {
        public TilingScheme TilingScheme;
        public SkirtMode SkirtMode;
        public MeshReconMethod ReconMethod;
        public int FacesPerTile;
        public int TileResolution;
        public string ProjectType;

        public CreateProjectMessage() { }
        public CreateProjectMessage(string projectName) : base(projectName) { }
    }

    public class DeleteProjectMessage : TilingQueueMessage
    {
        public DeleteProjectMessage() { }
        public DeleteProjectMessage(string projectName) : base(projectName) { }
    }

    public class AddInputMessage : TilingQueueMessage
    {
        public string Name;
        public string MeshUrl;
        public string ImageUrl;
        public string TileId;

        public AddInputMessage() { }
        public AddInputMessage(string projectName) : base(projectName) { }
    }

    public class RunProjectMessage : TilingQueueMessage
    {
        public RunProjectMessage() { }
        public RunProjectMessage(string projectName) : base(projectName) { }
    }

    public class TileCompletedMessage : TilingQueueMessage
    {
        public string TileId;

        public TileCompletedMessage(string projectName, string id) : base(projectName)
        {
            this.TileId = id;
        }
    }


}
