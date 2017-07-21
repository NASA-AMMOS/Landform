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
    /// Represents an image or 3D shape measurement of the environment
    /// Can be connected to Frames and aligned with other observations through
    /// FrameTransforms
    /// </summary>
    public class Observation
    {
        public int Id { get; set; }

        [Required]
        public string Url { get; set; }

        [Required]
        [Index("IX_ObservationUniqueness", 1, IsUnique = true)]
        public int ProjectId { get; set; }

        [Required]
        public int FrameId { get; set; }

        [Required]
        [MaxLength(255)]
        [Index("IX_ObservationUniqueness", 2, IsUnique = true)]
        public string Name { get; set; }

        [Required]
        public string ObservationType { get; set; }

        public string CameraModel { get; set; }

        public bool UseForReconstruction { get; set; }

        public Observation()
        {

        }

        /// <summary>
        /// Creates a new local observation object.  This object has an invalid id until it has been saved to the database context.
        /// Observation names must be unique within a project.
        /// ProjectId for this observation will be inferred from the supplied Frame object.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="frame"></param>
        /// <param name="url"></param>
        /// <param name="observationType"></param>
        /// <param name="cameraModel"></param>
        protected Observation(Frame frame, string name, string url, string observationType, string cameraModel, bool useForReconstruction)
        {
            if (!frame.HasValidId())
            {
                throw new CloudException("Cannot create observation with a frame that has not been saved to database.");
            }
            if(frame.ProjectId == 0)
            {
                throw new CloudException("Cannot create observation with unexpected project id found in frame");
            }            
            this.ProjectId = frame.ProjectId;
            this.Name = name;
            this.FrameId = frame.Id;
            this.Url = url;
            this.ObservationType = observationType;
            this.CameraModel = cameraModel;
            this.UseForReconstruction = useForReconstruction;
        }

        /// <summary>
        /// Creates a new observation and saves it to the database.  Returned observation has a valid id.
        /// Names must be unique within a project.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="frame"></param>
        /// <param name="name"></param>
        /// <param name="url"></param>
        /// <param name="observationType"></param>
        /// <param name="cameraModel"></param>
        /// <returns></returns>
        public static Observation Create(LandformDbContext context, Frame frame, string name, string url, string observationType, string cameraModel, bool useForReconstruction)
        {
            try
            {
                Observation observation = context.Observations.Add(new Observation(frame, name, url, observationType, cameraModel, useForReconstruction));
                context.SaveChanges();
                return observation;
            }
            catch (DbUpdateException)
            {
                // A record with this unique name and project id combination already exists
            }
            return null;
        }

        /// <summary>
        /// Finds an observation based on its name and project
        /// Return null if observation cannot be found
        /// </summary>
        /// <param name="context"></param>
        /// <param name="p"></param>
        /// <param name="name"></param>
        /// <returns></returns>
        public static Observation Find(LandformDbContext context, Project p, string name)
        {
            return context.Observations.Where(f => f.Name == name && f.ProjectId == p.Id).FirstOrDefault();
        }
    }
}
