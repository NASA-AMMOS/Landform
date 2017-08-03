//using OPS.Cloud.Migrations;
using MySql.Data.Entity;
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
    [DbConfigurationType(typeof(MySqlEFConfiguration))]
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
    }
}
