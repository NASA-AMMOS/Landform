using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Cloud;
using OPS.Plumbing;
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
        [DynamoDBHashKey] //Partition key
        [DynamoDBProperty()]
        public string Name { get; set; }

        /// <summary>
        /// Prefix of s3 URL where derived products should be saved. Format s3://<bucketname>/<prefix>/
        /// </summary>
        public string ProductPath { get; set; }
        /// <summary>
        /// Prefix of s3 URL where input data is stored. Format s3://<bucketname>/<prefix>/
        /// </summary>
        public string InputPath { get; set; }

        //This constructor must be public for DynamoDb but should not be used
        public Project()
        {
        }

        /// <summary>
        /// Creates Project object locally.  
        /// </summary>
        /// <param name="name">Project names in the database must be unique</param>
        protected Project(string name, string productPath, string inputPath)
        {
            Name = name;
            ProductPath = productPath;
            InputPath = inputPath;
            this.IsValid();
        }

        public static Project FindOrCreate(PipelineCore pipeline, string name, string productPath, string inputPath)
        {
            Project project = Find(pipeline, name);
            if (project != null)
            {
                return project;
            }

            project = Create(pipeline, name, productPath, inputPath);
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
        public static Project Create(PipelineCore pipeline, string name, string productPath, string inputPath)
        {
            Project project = new Project(name, productPath, inputPath);
            pipeline.SaveDatabaseItem(project);
            return project;
        }

        /// <summary>
        /// </summary>
        /// <returns></returns>
        public void Save(PipelineCore pipeline)
        {
            this.IsValid();
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

        private void IsValid()
        {
            if (!(Name != null && ProductPath != null && InputPath != null))
            {
                throw new CloudException("Project is missing a required field");
            }
        }
    }
}
