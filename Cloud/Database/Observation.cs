using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Amazon.DynamoDBv2.DataModel;

namespace OPS.Cloud
{
    /// <summary>
    /// Represents an image or 3D shape measurement of the environment
    /// Can be connected to Frames and aligned with other observations through
    /// FrameTransforms
    /// </summary>
    [DynamoDBTable("Observations")]
    public class Observation
    {
        [DynamoDBRangeKey]
        [DynamoDBProperty("project_name")]
        public string ProjectName { get; set; }

        [DynamoDBHashKey] //Partition key
        [DynamoDBProperty("observation_name")]
        public string Name { get; set; }

        public string Url { get; set; }

        public string FeatureUrl { get; set; }

        public string FrameName { get; set; }

        public string ObservationType { get; set; }

        public string CameraModel { get; set; }

        public bool UseForReconstruction { get; set; }

        [DynamoDBVersion]
        public int? VersionNumber { get; set; }

        /// Add required fields here 
        private void IsValid()
        {
            if (!(Url != null &&
                FrameName != null &&
                ProjectName != null &&
                Name != null &&
                ObservationType != null))
            {
                throw new CloudException("Missing required property in Observation");
            }
        }

        //This constructor must be public for DynamoDb but should not be used
        public Observation()
        {
            
        }

        /// <summary>
        /// Creates a new local observation object.  
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
            this.ProjectName = frame.ProjectName;
            this.FrameName = frame.Name;
            this.Name = name;
            this.Url = url;
            this.ObservationType = observationType;
            this.CameraModel = cameraModel;
            this.UseForReconstruction = useForReconstruction;
            IsValid();
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
        public static Observation Create(DynamoDBContext context, Frame frame, string name, string url, string observationType, string cameraModel, bool useForReconstruction)
        {
            if (Find(context, frame.ProjectName, name) != null)
            {
                return null; // A record with this unique name already exists
            }
            Observation obs = new Observation(frame, name, url, observationType, cameraModel, useForReconstruction);
            
            context.Save(obs);
            return obs;
        }

        /// <summary>
        /// Finds an observation based on its name and project
        /// Return null if observation cannot be found
        /// </summary>
        /// <param name="context"></param>
        /// <param name="imageId"></param>
        /// <returns></returns>
        public static Observation Find(DynamoDBContext context, string projectName, string name)
        {
            return context.Load<Observation>(name, projectName);
        }
    }
}
