using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.Model;
using Amazon.DynamoDBv2.DocumentModel;

namespace OPS.Cloud
{

    /// <summary>
    /// Represents a coordinate frame in the database
    /// Coordiante frames can have one or more observations associated with them. 
    /// Frames are not versioned
    /// </summary>\
    [DynamoDBTable("Frames")]
    public class Frame
    {
        [DynamoDBRangeKey]
        [DynamoDBProperty()]
        public string ProjectName { get; set; }

        [DynamoDBHashKey] //Partition key
        [DynamoDBProperty("FrameName")]
        public string Name { get; set; }

        [DynamoDBProperty()]
        public string ParentName { get; set; }

        [DynamoDBProperty()]
        public List<string> PriorIds { get; set; }

        //This constructor must be public for DynamoDb but should not be used
        public Frame()
        {
            PriorIds = new List<string>();
        }

        public IEnumerable<Frame> GetChildren(DynamoDBContext context)
        {
            return context.Scan<Frame>(
                new ScanCondition("ProjectName", ScanOperator.Equal, ProjectName),
                new ScanCondition("ParentName", ScanOperator.Equal, Name));
        }

        public Frame GetParent(DynamoDBContext context)
        {
            if (ParentName == null) return null;
            return Find(context, ProjectName, ParentName);
        }

        /// <summary>
        /// Creates a local instance of a frame.  The frame will have an invalid id
        /// until it is saved to the database's context.
        /// Frame names must be unique within a project.  If no name is specified a random GUID will used.
        /// </summary>
        /// <param name="project">Project with a valid id (has been saved to database context)</param>
        /// <param name="name"></param>
        protected Frame(Project project, string name = null, Frame parent = null)
        {
            if (name == null)
            {
                name = Guid.NewGuid().ToString();
            }
            this.Name = name;
            this.ProjectName = project.Name;
            this.ParentName = (parent != null) ? parent.Name : null;
            this.PriorIds = new List<string>();
        }


        /// <summary>
        /// Creates a frame for the given project with the given name.  If no name is specifed a random GUID will be used.
        /// Saves the frame the the database and returns an object with a valid id.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="p">Project with a valid id (has been saved to database context)</param>
        /// <param name="name"></param>
        /// <returns></returns>
        public static Frame Create(DynamoDBContext context, Project p, string name = null, Frame parent = null)
        {
            if (name == null)
            {
                name = Guid.NewGuid().ToString();
            }
            Frame f = new Frame(p, name, parent);
            context.Save<Frame>(f, new DynamoDBOperationConfig { IgnoreNullValues = true});
            return f;
        }

        /// <summary>
        /// Save this observation without overwriting any values it may be missing
        /// </summary>
        /// <param name=""></param>
        public void Save(DynamoDBContext context)
        {
            context.Save(this, new DynamoDBOperationConfig { IgnoreNullValues = true });
        }

        /// <summary>
        /// Find a frame in the given project with the specififed name.  Create it if it doesn't exist.
        /// Returns the frame if it can be found or created.  Returns null otherwise.
        /// Returned frame is saved in the database and has a valid id.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="p">Project with a valid id (has been saved to database context)</param>
        /// <param name="name"></param>
        /// <returns></returns>
        public static Frame FindOrCreate(DynamoDBContext context, Project p, string name, Frame parent = null)
        {
            // Try to find this project
            Frame frame = Find(context, p.Name, name);
            if (frame != null)
            {
                return frame;
            }
            // If it doesn't exist try to create it
            frame = Create(context, p, name, parent);
            if (frame != null)
            {
                return frame;
            }
            // If our create failed someone else may have created one between our find and create calls
            // Look for it again.
            return Find(context, p.Name, name);
        }

        /// <summary>
        /// Find a frame in the database with the specified project and name.  Returns null if none exists.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="p">Project with a valid id (has been saved to database context)</param>
        /// <param name="name"></param>
        /// <returns></returns>
        public static Frame Find(DynamoDBContext context, string projectName, string name)
        {
            return context.Load<Frame>(name, projectName);
        }
    }   
}
