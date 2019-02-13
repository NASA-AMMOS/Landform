using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MathNet.Numerics.LinearAlgebra;
using OPS.Geometry;
using OPS.Pipeline.AlignmentServer;

namespace OPS.Pipeline
{
    public class InitializeAlignmentProject : PipelineRoutine
    {
        public InitializeAlignmentProject(PipelineCore pipeline) : base(pipeline) { }

        public Project Initialize(string projectName, string productPath, string inputPath, bool recreateIfExists,
                                  string rootName, out Frame rootFrame, out FrameTransform rootTransform)
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

                rootFrame = Frame.Find(pipeline, projectName, rootName);
                if (rootFrame != null)
                {
                    rootTransform = FrameTransform.Find(pipeline, rootFrame);
                    if (rootTransform != null)
                    {
                        pipeline.DeleteDatabaseItem(rootTransform);
                    }
                    pipeline.DeleteDatabaseItem(rootFrame);
                    rootFrame = null;
                    rootTransform = null;
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

            rootFrame = Frame.FindOrCreate(pipeline, projectName, rootName);

            var xform = new UncertainRigidTransform
                (new MathExtensions.GaussianND(CreateVector.Dense<double>(6), CreateMatrix.Dense<double>(6, 6)));

            rootTransform = FrameTransform.FindOrCreate(pipeline, rootFrame, xform);

            return project;
        }

        public Project Initialize(string projectName, string productPath, string inputPath, bool recreateIfExists)
        {
            Frame rootFrame = null;
            FrameTransform rootTransform = null;
            return Initialize(projectName, productPath, inputPath, recreateIfExists, MSLProject.ROOT_FRAME_NAME,
                              out rootFrame, out rootTransform);
        }
    }
}
