using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2;

namespace OPS.Cloud
{
    /// <summary>
    /// A project specifies a container for a 3D reconstruction consiting of mutliple observations
    /// </summary>
    [DynamoDBTable("Projects")]
    public class Project
    {
        [DynamoDBHashKey] //Partition key
        [DynamoDBProperty("project_name")]
        public string Name { get; set; }

        [DynamoDBVersion]
        public int? VersionNumber { get; set; }

        //This constructor must be public for DynamoDb but should not be used
        public Project()
        {

        }

        /// <summary>
        /// Creates Project object locally.  The project's Id field will be invalid
        /// until it is added to the database context and the context is saved.
        /// </summary>
        /// <param name="name">Project names in the database must be unique</param>
        protected Project(string name)
        {
            this.Name = name;
        }

        /// <summary>
        /// Creates a project and saves it in the database.  Returns a project object with a valid id.
        /// Returns null if a project with this name already exists in the database.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="name">Project names in the database must be unique</param>
        /// <returns></returns>
        public static Project Create(DynamoDBContext context, string name)
        {
            if (Find(context, name) != null)
            {
                return null; //project already exists
            }
            Project project = new Project(name);
            context.Save(project);
            return project;
        }

        /// <summary>
        /// Finds a project matching this name.  If one doesn't exist then it is created and returned.
        /// Returned project is saved in database and has a valid id.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="name">Project names in the database must be unique</param>
        /// <returns></returns>
        public static Project FindOrCreate(DynamoDBContext context, string name)
        {
            // Try to find this project
            Project project = Find(context, name);
            if (project != null)
            {
                return project;
            }
            // If it doesn't exist try to create it
            project = Create(context, name);
            if(project != null)
            {
                return project;
            }
            // If our create failed someone else may have created one between our find and create calls
            // Look for it again.
            return Find(context, name); 
        }

        /// <summary>
        /// Searches for a project with the given name from the database.
        /// Returns null if it doesn't exist.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="name">Project names in the database must be unique</param>
        /// <returns></returns>
        public static Project Find(DynamoDBContext context, string name)
        {
            return context.Load<Project>(name);
        }
    }
}
