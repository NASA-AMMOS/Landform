using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Cloud;
using Amazon.DynamoDBv2.DataModel;

namespace OPS.Pipeline.AlignmentServer
{

    /// <summary>
    /// Represents a coordinate frame in the database
    /// Coordinate frames can have one or more observations associated with them. 
    /// Frames are not versioned
    /// </summary>\
    [DynamoDBTable("Frames")]
    [DynamoDBReadCapacity(5, 50)]
    [DynamoDBWriteCapacity(5, 50)]
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

        public bool IsLocated(PipelineCore pipeline)
        {
            return FrameTransform.Find(pipeline, this) != null;
        }

        //This constructor must be public for DynamoDb but should not be used
        public Frame()
        {
            PriorIds = new List<string>();
        }

        public IEnumerable<Frame> GetChildren(PipelineCore pipeline)
        {
            return pipeline.ScanDatabase<Frame>("ProjectName", ProjectName, "ParentName", Name);
        }

        public Frame GetParent(PipelineCore pipeline)
        {
            if (ParentName == null) return null;
            return Find(pipeline, ProjectName, ParentName);
        }

        /// <summary>
        /// Creates a local instance of a frame.  The frame will have an invalid id
        /// until it is saved to the database.
        /// Frame names must be unique within a project.  If no name is specified a random GUID will used.
        /// </summary>
        /// <param name="projectName"></param>
        /// <param name="name"></param>
        protected Frame(string projectName, string name = null, Frame parent = null)
        {
            if (name == null)
            {
                name = Guid.NewGuid().ToString();
            }
            this.Name = name;
            this.ProjectName = projectName;
            this.ParentName = (parent != null) ? parent.Name : null;
            this.PriorIds = new List<string>();
        }


        /// <summary>
        /// Creates a frame for the given project with the given name.  If no name is specifed a random GUID will be used.
        /// Saves the frame the the database and returns an object with a valid id.
        /// </summary>
        /// <param name="pipeline"></param>
        /// <param name="projectName"></param>
        /// <param name="name"></param>
        /// <returns></returns>
        public static Frame Create(PipelineCore pipeline, string projectName, string name = null, Frame parent = null)
        {
            if (name == null)
            {
                name = Guid.NewGuid().ToString();
            }
            Frame f = new Frame(projectName, name, parent);
            pipeline.SaveDatabaseItem<Frame>(f);
            return f;
        }

        /// <summary>
        /// Save this observation without overwriting any values it may be missing
        /// </summary>
        /// <param name=""></param>
        public void Save(PipelineCore pipeline)
        {
            pipeline.SaveDatabaseItem(this);
        }

        /// <summary>
        /// Find a frame in the given project with the specififed name.  Create it if it doesn't exist.
        /// Returns the frame if it can be found or created.  Returns null otherwise.
        /// Returned frame is saved in the database and has a valid id.
        /// </summary>
        /// <param name="pipeline"></param>
        /// <param name="projectName"></param>
        /// <param name="name"></param>
        /// <returns></returns>
        public static Frame FindOrCreate(PipelineCore pipeline, string projectName, string name, Frame parent = null)
        {
            // Try to find this project
            Frame frame = Find(pipeline, projectName, name);
            if (frame != null)
            {
                return frame;
            }
            // If it doesn't exist try to create it
            frame = Create(pipeline, projectName, name, parent);
            if (frame != null)
            {
                return frame;
            }
            // If our create failed someone else may have created one between our find and create calls
            // Look for it again.
            return Find(pipeline, projectName, name);
        }

        /// <summary>
        /// Find a frame in the database with the specified project and name.  Returns null if none exists.
        /// </summary>
        /// <param name="pipeline"></param>
        /// <param name="p">Project with a valid id (has been saved to database)</param>
        /// <param name="name"></param>
        /// <returns></returns>
        public static Frame Find(PipelineCore pipeline, string projectName, string name)
        {
            return pipeline.LoadDatabaseItem<Frame>(name, projectName);
        }

        public static IEnumerable<Frame> Find(PipelineCore pipeline, string projectName)
        {
            return pipeline.ScanDatabase<Frame>("ProjectName", projectName);
        }
    }   
}
