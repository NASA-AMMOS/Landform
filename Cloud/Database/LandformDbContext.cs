//using OPS.Cloud.Migrations;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Cloud
{
    /// <summary>
    /// Entity Framework context object for the landform database
    /// </summary>
    public class LandformDbContext : DbContext
    {
        public DbSet<Project> Projects { get; set; }
        public DbSet<Observation> Observations { get; set; }
        public DbSet<Frame> Frames { get; set; }
        public DbSet<FrameTransform> FrameTransforms { get; set; }

        /// <summary>
        /// Needed by some Entity Framework command line calls when working with a local database
        /// </summary>
        public LandformDbContext() : base() { }

        /// <summary>
        /// Create the context
        /// </summary>
        /// <param name="str">Connection string used to connect to database</param>
        public LandformDbContext(string str) : base(str)
        {
        }

        public Project FindOrCreateProject(string name)
        {
            Project project = Projects.Where(p => p.Name == name).FirstOrDefault();
            if (project == null)
            {
                project = Projects.Add(new Project(name));
                SaveChanges();
            }           
            return project;
        }

        public Frame FindOrCreateFrame(Project p, string name)
        {
            Frame frame = Frames.Where(f => f.Name == name && f.ProjectId == p.Id).FirstOrDefault();
            if (frame == null)
            {
                frame = Frames.Add(new Frame(p, name));
                SaveChanges();
            }
            return frame;
        }

        public Observation FindObservation(Project p, string name)
        {
            return Observations.Where(obs => obs.Name == name && obs.ProjectId == p.Id).FirstOrDefault();
        }

        public Observation AddObservation(Observation observation)
        {           
            observation = Observations.Add(observation);
            SaveChanges();
            return observation;
        }

    }
}
