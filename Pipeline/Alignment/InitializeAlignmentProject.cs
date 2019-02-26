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
        public InitializeAlignmentProject(PipelineCore pipeline) : base(pipeline) { }

        public Project Initialize(string projectName, string productPath, string inputPath, bool recreateIfExists,
                                  string rootName)
        {
            var project = Project.Find(pipeline, projectName);

            if ((project == null || recreateIfExists) && string.IsNullOrEmpty(inputPath))
            {
                throw new ArgumentException("input path must be specified to (re)create project");
            }

            if (project == null)
            {
                pipeline.LogInfo("creating alignment project {0}", projectName);
                project = Project.Create(pipeline, projectName, productPath, inputPath, rootName);
            }
            else if (recreateIfExists)
            {
                pipeline.LogInfo("re-creating alignment project {0}", projectName);

                pipeline.DeleteDatabaseItem(project);
                project = Project.Create(pipeline, projectName, productPath, inputPath, rootName);

                var oldRoot = Frame.Find(pipeline, projectName, rootName);
                if (oldRoot != null)
                {
                    foreach (var source in oldRoot.Transforms)
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

        public Project Initialize(string projectName, string productPath, string inputPath, bool recreateIfExists)
        {
            return Initialize(projectName, productPath, inputPath, recreateIfExists, MSLProject.ROOT_FRAME_NAME);
        }
    }
}
