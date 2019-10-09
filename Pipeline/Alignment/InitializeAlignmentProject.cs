using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Geometry;
using OPS.Pipeline.AlignmentServer;

namespace OPS.Pipeline
{
    public class InitializeAlignmentProject : PipelineRoutine
    {
        public const string DATA_PRODUCT_DIR = "alignment/products";

        public InitializeAlignmentProject(PipelineCore pipeline) : base(pipeline) { }

        public Project Initialize(string projectName, string productPath, string inputPath, Mission mission,
                                  bool recreateIfExists)
        {
            Project project = null;
            try
            {
                Project.Find(pipeline, projectName);
            }
            catch (Exception ex)
            {
                if (!recreateIfExists)
                {
                    throw;
                }
                else
                {
                    pipeline.LogWarn("error loading existing project \"{0}\", recreating: {1}",
                                     projectName, ex.Message);
                }
            }


            string rootName = MissionSpecific.GetInstance(mission).RootFrameName();

            if (project == null)
            {
                pipeline.LogInfo("creating alignment project {0}", projectName);
                project = Project.Create(pipeline, projectName, productPath, inputPath, mission.ToString());
            }
            else if (recreateIfExists)
            {
                pipeline.LogInfo("re-creating alignment project {0}", projectName);

                pipeline.DeleteDatabaseItem(project);
                project = Project.Create(pipeline, projectName, productPath, inputPath, mission.ToString());

                var oldRoot = Frame.Find(pipeline, projectName, rootName);
                if (oldRoot != null)
                {
                    IEnumerable<TransformSource> transforms = null;
                    lock (oldRoot.Transforms)
                    {
                        transforms = oldRoot.Transforms.ToArray();
                    }
                    foreach (var source in transforms)
                    {
                        var transform = FrameTransform.Find(pipeline, oldRoot, source);
                        if (transform != null)
                        {
                            pipeline.DeleteDatabaseItem(transform);
                        }
                    }
                }
            }
            else
            {
                if (productPath != null && project.ProductPath != productPath)
                {
                    throw new Exception(string.Format("alignment project {0} already exists " +
                                                      "but has product path \"{1}\", not \"{2}\"",
                                                      projectName, project.ProductPath, productPath));
                }
                if (inputPath != null && project.InputPath != inputPath)
                {
                    throw new Exception(string.Format("alignment project {0} already exists " +
                                                      "but has input path \"{1}\", not \"{2}\"",
                                                      projectName, project.InputPath, inputPath));
                }
                pipeline.LogInfo("using existing alignment project {0}", projectName);
            }

            var rootFrame = Frame.FindOrCreate(pipeline, projectName, rootName);
            var ut = new UncertainRigidTransform(); //identity, certain
            FrameTransform.FindOrCreate(pipeline, rootFrame, TransformSource.Prior, ut);

            return project;
        }
    }
}
