using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Cloud;
using Amazon.DynamoDBv2.DataModel;

namespace OPS.Pipeline.AlignmentServer
{
    public enum ObservationType
    {
        Image,
        Points,
        Normals,
        RoverMask
    }

    /// <summary>
    /// Represents an image or 3D shape measurement of the environment
    /// Can be connected to Frames and aligned with other observations through FrameTransforms
    /// Observations are not versioned, because all of the data associated with them is deterministic, so it does not matter if workers re-upload them. 
    /// Fresh Creates, or Saves with missing values, will not overwrite existing values. 
    /// </summary>
    [DynamoDBTable("Observations")]
    [DynamoDBReadCapacity(50, 100)]
    [DynamoDBWriteCapacity(50, 100)]
    public class Observation
    {
        [DynamoDBRangeKey]
        public string ProjectName;

        [DynamoDBHashKey]
        public string Name;

        public string Url;

        public Guid FeaturesGuid;

        public string FrameName;

        public string ObservationType;

        public string CameraModel;

        public bool UseForReconstruction;

        public int Width;

        public int Height;

        //DEPRECATED - for legacy compat only
        public string MaskGuid;

        //DEPRECATED - for legacy compat only
        public string FeatureUrl;

        /// Add required fields here 
        protected void IsValid()
        {
            if (!(Url != null &&
                FrameName != null &&
                ProjectName != null &&
                Name != null &&
                ObservationType != null))
            {
                throw new Exception("Missing required property in Observation");
            }
        }

        //This constructor must be public for DynamoDb but should not be used
        public Observation() { }

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
        protected Observation(Frame frame, string name, string url, string observationType, string cameraModel, bool useForReconstruction, int width, int height)
        {
            this.ProjectName = frame.ProjectName;
            this.FrameName = frame.Name;
            this.Name = name;
            this.Url = url;
            this.ObservationType = observationType;
            this.CameraModel = cameraModel;
            this.UseForReconstruction = useForReconstruction;
            this.Width = width;
            this.Height = height;
            IsValid();
        }

        /// <summary>
        /// Creates a new observation and saves it to the database.  Returned observation has a valid id.
        /// Names must be unique within a project.
        /// </summary>
        /// <param name="pipeline"></param>
        /// <param name="frame"></param>
        /// <param name="name"></param>
        /// <param name="url"></param>
        /// <param name="observationType"></param>
        /// <param name="cameraModel"></param>
        /// <returns></returns>
        public static Observation Create(PipelineCore pipeline, Frame frame, string name, string url, string observationType, string cameraModel, bool useForReconstruction, int width, int height)
        {
            Observation obs = new Observation(frame, name, url, observationType, cameraModel, useForReconstruction, width, height);
            obs.Save(pipeline);
            return obs;
        }

        /// <summary>
        /// Save this observation without overwriting any values it may be missing
        /// </summary>
        /// <param name=""></param>
        public virtual void Save(PipelineCore pipeline)
        {
            IsValid();
            pipeline.SaveDatabaseItem(this);
        }

        /// <summary>
        /// Finds an observation based on its name and project
        /// Return null if observation cannot be found
        /// </summary>
        /// <param name="pipeline"></param>
        /// <param name="imageId"></param>
        /// <returns></returns>
        public static Observation Find(PipelineCore pipeline, string projectName, string name)
        {
            return pipeline.LoadDatabaseItem<Observation>(name, projectName);
        }

        public static IEnumerable<Observation> Find(PipelineCore pipeline, string projectName)
        {
            return pipeline.ScanDatabase<Observation>("ProjectName", projectName);
        }

        public static IEnumerable<Observation> Find(PipelineCore pipeline, Frame frame)
        {
            //we could do a scan here, but it's better to avoid it
            //because it will by definition iterate over every single Observation in the database
            //return pipeline.ScanDatabase<Observation>("ProjectName", frame.ProjectName, "FrameName", frame.Name);
            foreach (var obsName in frame.ObservationNames)
            {
                yield return Find(pipeline, frame.ProjectName, obsName);
            }
        }

        public static IEnumerable<Observation> FindByType(PipelineCore pipeline, string projectName, string observationType)
        {
            return pipeline.ScanDatabase<Observation>("ProjectName", projectName, "ObservationType", observationType); 
        }
    }
}
