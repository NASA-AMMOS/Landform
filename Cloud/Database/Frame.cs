using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Amazon.DynamoDBv2.DataModel;

namespace OPS.Cloud
{

    /// <summary>
    /// Represents a coordinate frame in the database
    /// Coordiante frames can have one or more observations associated with them
    /// </summary>\
    [DynamoDBTable("mango-Frames-1VOU76GJPKP27")]
    public class Frame
    {
        public int Id { get; set; }

        [Required]
        [Index("IX_FrameUniqueness", 1, IsUnique = true)]
        public int ProjectId { get; set; }

        [DynamoDBRangeKey]
        [DynamoDBProperty("project_name")]
        public string ProjectName { get; set; }

        [Index("IX_FrameUniqueness", 2, IsUnique = true)]
        [MaxLength(255)]
        [DynamoDBHashKey] //Partition key
        [DynamoDBProperty("frame_name")]
        public string Name { get; set; }

        public Frame()
        {

        }

        /// <summary>
        /// Creates a local instance of a frame.  The frame will have an invalid id
        /// until it is saved to the database's context.
        /// Frame names must be unique within a project.  If no name is specified a random GUID will used.
        /// </summary>
        /// <param name="project">Project with a valid id (has been saved to database context)</param>
        /// <param name="name"></param>
        protected Frame(Project project, string name = null)
        {
            //TODO: the equivalent thing in Dynamo is to use version numbers (optimistic locking) and check that version number > 0
            if(!project.HasValidId())
            {
                throw new CloudException("Cannot create frame with a project that has not been saved to database.");
            }
            if(name == null)
            {
                name = Guid.NewGuid().ToString();
            }
            this.Name = name;
            this.ProjectId = project.Id;
            this.ProjectName = project.Name;
        }


        /// <summary>
        /// Creates a frame for the given project with the given name.  If no name is specifed a random GUID will be used.
        /// Saves the frame the the database and returns an object with a valid id.
        /// Returns null if a frame with the given name already exists for this project. -- TODO not implemented
        /// TODO optimistic locking to return null
        /// </summary>
        /// <param name="context"></param>
        /// <param name="p">Project with a valid id (has been saved to database context)</param>
        /// <param name="name"></param>
        /// <returns></returns>
        public static Frame Create(DynamoDBContext context, Project p, string name = null)
        {
            if (name == null)
            {
                name = Guid.NewGuid().ToString();
            }
            Frame f = new Frame(p, name);
            f.Id = 1; //so later checks give us "valid id"
            context.Save<Frame>(f);
            return f;
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
        public static Frame FindOrCreate(DynamoDBContext context, Project p, string name)
        {
            // Try to find this project
            Frame frame = Find(context, p, name);
            if (frame != null)
            {
                return frame;
            }
            // If it doesn't exist try to create it
            frame = Create(context, p, name);
            if (frame != null)
            {
                return frame;
            }
            // If our create failed someone else may have created one between our find and create calls
            // Look for it again.
            return Find(context, p, name);
        }

        /// <summary>
        /// Find a frame in the database with the specified project and name.  Returns null if none exists.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="p">Project with a valid id (has been saved to database context)</param>
        /// <param name="name"></param>
        /// <returns></returns>
        public static Frame Find(DynamoDBContext context, Project p, string name)
        {
            return context.Load<Frame>(name, p.Name);
        }

        /// <summary>
        /// Returns true if Id is valid (this object has been saved to the database)
        /// </summary>
        /// <returns></returns>
        public bool HasValidId()
        {
            return Id != 0;
        }
    }   
}
