using System;
using System.Collections.Generic;
using System.Linq;

namespace JPLOPS.Pipeline.AlignmentServer
{
    /// <summary>
    /// A project specifies a container for a 3D reconstruction consiting of mutliple observations
    /// </summary>
    public class Project
    {
        [DBHashKey]
        public string Name;

        public string Mission;

        public string MeshFrame;

        public string ProductPath;

        public string InputPath;

        public HashSet<string> SceneMeshes = new HashSet<string>(); //MT safety: lock before accessing

        private void IsValid()
        {
            if (!(Name != null && Mission != null))
            {
                throw new Exception("Project is missing a required field");
            }
        }

        public Project() { }

        /// <summary>
        /// Creates Project  
        /// </summary>
        /// <param name="name">Project names in the database must be unique</param>
        protected Project(string name, string mission, string meshFrame, string productPath, string inputPath)
        {
            Name = name;
            Mission = mission;
            MeshFrame = meshFrame;
            ProductPath = productPath;
            InputPath = inputPath;
            IsValid();
        }

        public static Project FindOrCreate(PipelineCore pipeline, string name, string mission, string meshFrame,
                                           string productPath, string inputPath)
        {
            Project project = Find(pipeline, name);
            if (project != null)
            {
                return project;
            }

            project = Create(pipeline, name, mission, meshFrame, productPath, inputPath);
            if (project != null)
            {
                return project;
            }

            // may have been created by someone else inbetween the query and the create
            return Find(pipeline, name);
        }

        /// <summary>
        /// Creates a project and saves it in the database.
        /// </summary>
        /// <param name="pipeline"></param>
        /// <param name="name">Project names in the database must be unique</param>
        /// <returns></returns>
        public static Project Create(PipelineCore pipeline, string name, string mission, string meshFrame,
                                     string productPath, string inputPath)
        {
            Project project = new Project(name, mission, meshFrame, productPath, inputPath);
            project.Save(pipeline);
            return project;
        }

        /// <summary>
        /// </summary>
        /// <returns></returns>
        public void Save(PipelineCore pipeline)
        {
            IsValid();
            pipeline.SaveDatabaseItem(this);
        }

        /// <summary>
        /// Searches for a project with the given name from the database.
        /// Returns null if it doesn't exist.
        /// </summary>
        /// <param name="pipeline"></param>
        /// <param name="name">Project names in the database must be unique</param>
        /// <returns></returns>
        public static Project Find(PipelineCore pipeline, string name)
        {
            Project project = pipeline.LoadDatabaseItem<Project>(name);
            if (project != null)
            {
                project.IsValid();
            }
            return project;
        }

        public IEnumerable<string> GetSceneMeshes()
        {
            IEnumerable<string> ret = null;
            lock (SceneMeshes)
            {
                ret = SceneMeshes.ToArray();
            }
            return ret;
        }
    }
}
