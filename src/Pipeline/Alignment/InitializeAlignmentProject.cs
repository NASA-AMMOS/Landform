using System;
using System.Linq;
using System.Collections.Generic;
using JPLOPS.Geometry;
using JPLOPS.Pipeline.AlignmentServer;

namespace JPLOPS.Pipeline
{
    public class InitializeAlignmentProject
    {
        public const string DATA_PRODUCT_DIR = "alignment/products";

        public readonly PipelineCore pipeline;

        public InitializeAlignmentProject(PipelineCore pipeline)
        {
            this.pipeline = pipeline;
        }

        public Project Initialize(string projectName, string mission, string meshFrame,
                                  string productPath, string inputPath, bool recreateIfExists = false)
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
                pipeline.LogInfo("creating alignment project {0}: " +
                                 "mission {1}, mesh frame {2}, product path {3}, input path {4}",
                                 projectName, mission, meshFrame, productPath, inputPath);
                project = Project.Create(pipeline, projectName, mission.ToString(), meshFrame, productPath, inputPath);
            }
            else if (recreateIfExists)
            {
                pipeline.LogInfo("re-creating alignment project {0}", projectName);

                pipeline.DeleteDatabaseItem(project);
                project = Project.Create(pipeline, mission.ToString(), meshFrame, projectName, productPath, inputPath);

                var oldRoot = Frame.Find(pipeline, projectName, rootName);
                if (oldRoot != null)
                {
                    IEnumerable<TransformSource> transforms = null;
                    lock (oldRoot.Transforms)
                    {
                        transforms = oldRoot.Transforms.ToArray();
                    }
                    foreach (var ts in transforms)
                    {
                        var transform = FrameTransform.Find(pipeline, oldRoot, ts);
                        if (transform != null)
                        {
                            pipeline.DeleteDatabaseItem(transform);
                        }
                    }
                }
            }
            else
            {
                if (mission.ToString() != project.Mission)
                {
                    throw new Exception(string.Format("alignment project {0} already exists " +
                                                      "but has mission \"{1}\", not \"{2}\"",
                                                      projectName, project.Mission, mission));
                }

                if (meshFrame != project.MeshFrame)
                {
                    throw new Exception(string.Format("alignment project {0} already exists " +
                                                      "but has mesh frame \"{1}\", not \"{2}\"",
                                                      projectName, project.MeshFrame, meshFrame));
                }

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

            var source = TransformSource.Prior; 
            var identity = new UncertainRigidTransform();
            var rootFrame = Frame.FindOrCreate(pipeline, projectName, rootName); //saves
            FrameTransform.FindOrCreate(pipeline, rootFrame, source, identity); //saves

            return project;
        }
    }
}
