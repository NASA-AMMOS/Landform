using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Cloud
{

    /// <summary>
    /// Represents a coordinate frame in the database
    /// Coordiante frames can have one or more observations associated with them
    /// </summary>
    public class Frame
    {
        public int Id { get; set; }
        [Required]
        [Index("IX_FrameUniqueness", 1, IsUnique = true)]
        public int ProjectId { get; set; }
        [Index("IX_FrameUniqueness", 2, IsUnique = true)]
        [MaxLength(255)]
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
        }


        /// <summary>
        /// Creates a frame for the given project with the given name.  If no name is specifed a random GUID will be used.
        /// Saves the frame the the database and returns an object with a valid id.
        /// Returns null if a frame with the given name already exists for this project.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="p">Project with a valid id (has been saved to database context)</param>
        /// <param name="name"></param>
        /// <returns></returns>
        public static Frame Create(LandformDbContext context, Project p, string name = null)
        {
            try
            {
                Frame frame = context.Frames.Add(new Frame(p, name));
                context.SaveChanges();
                return frame;
            }
            catch (DbUpdateException)
            {
                // A record with this unique name and project id combination already exists
            }
            return null;
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
        public static Frame FindOrCreate(LandformDbContext context, Project p, string name)
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
        public static Frame Find(LandformDbContext context, Project p, string name)
        {
            return context.Frames.Where(f => f.Name == name && f.ProjectId == p.Id).FirstOrDefault();
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
