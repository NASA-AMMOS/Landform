using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Cloud;
using OPS.Pipeline;
using Amazon.DynamoDBv2.DataModel;

namespace OPS.Pipeline.AlignmentServer
{
    /// <summary>
    /// A project specifies a container for a 3D reconstruction consiting of mutliple observations
    /// </summary>
    [DynamoDBTable("Projects")]
    [DynamoDBReadCapacity(5, 50)]
    [DynamoDBWriteCapacity(5, 50)]
    public class Project
    {
        [DynamoDBHashKey]
        public string Name;

        public string ProductPath;

        public string InputPath;

        public string Mission;

        private void IsValid()
        {
            if (!(Name != null && Mission != null))
            {
                throw new Exception("Project is missing a required field");
            }
        }

        //This constructor must be public for DynamoDB but should not be used
        public Project() { }

        /// <summary>
        /// Creates Project  
        /// </summary>
        /// <param name="name">Project names in the database must be unique</param>
        protected Project(string name, string productPath, string inputPath, string mission)
        {
            Name = name;
            ProductPath = productPath;
            InputPath = inputPath;
            Mission = mission;
            IsValid();
        }

        public static Project FindOrCreate(PipelineCore pipeline, string name, string productPath, string inputPath,
                                           string mission)
        {
            Project project = Find(pipeline, name);
            if (project != null)
            {
                return project;
            }

            project = Create(pipeline, name, productPath, inputPath, mission);
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
        public static Project Create(PipelineCore pipeline, string name, string productPath, string inputPath,
                                     string mission)
        {
            Project project = new Project(name, productPath, inputPath, mission);
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
                if (pipeline.LegacyCompat && string.IsNullOrEmpty(project.Mission))
                {
                    project.Mission = OPS.Pipeline.Mission.MSL.ToString(); //legacy compat
                }
                project.IsValid();
            }
            return project;
        }
    }
}
