using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Cloud;
using OPS.Imaging;
using OPS.Util;
using Amazon.DynamoDBv2.DataModel;

namespace OPS.Pipeline.AlignmentServer
{
    public enum TextureVariant { Original, Blurred, Blended };

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
        //index 0 is reserved to mean "no observation"
        //also note, in legacy TerrainTools index 65535 (0xffff) is treated equivalent to 0 in LimberDMG
        //and those values can get serialized out to the index image for pixels where backprojection failed
        public const int MIN_INDEX = 1;

        //limit indices to unsigned ints that can be exactly represented in a float
        //https://stackoverflow.com/a/3793950
        //this makes it possible to store an observation index in one band of a float image
        //and we want to do that when creating backproject index images
        public const int MAX_INDEX = 16777216;

        [DynamoDBRangeKey]
        public string ProjectName;

        [DynamoDBHashKey]
        public string Name;

        public string Url;

        public Guid MaskGuid; //combines rover mask, user mask, invalid/missing pixels, and border

        public Guid FeaturesGuid;

        public Guid BlurredGuid;

        public Guid BlendedGuid;

        public string FrameName;

        public string CameraModel;

        public bool UseForReconstruction;

        public int Width;

        public int Height;

        public int Bands;

        public int Bits;

        public int Day;

        public int Version;

        public int Index;

        /// Add required fields here 
        protected void IsValid()
        {
            if (!(Url != null && FrameName != null && ProjectName != null && Name != null))
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
        protected Observation(Frame frame, string name, string url, string cameraModel,
                              bool useForReconstruction, int width, int height, int bands, int bits, int day,
                              int version, int index)
        {
            this.ProjectName = frame.ProjectName;
            this.FrameName = frame.Name;
            this.Name = name;
            this.Url = url;
            this.MaskGuid = Guid.Empty;
            this.FeaturesGuid = Guid.Empty;
            this.BlurredGuid = Guid.Empty;
            this.BlendedGuid = Guid.Empty;
            this.CameraModel = cameraModel;
            this.UseForReconstruction = useForReconstruction;
            this.Width = width;
            this.Height = height;
            this.Bands = bands;
            this.Bits = bits;
            this.Day = day;
            this.Version = version;
            this.Index = index;
            IsValid();
        }

        /// <summary>
        /// Creates a new observation and saves it to the database.  Returned observation has a valid id.
        /// Names must be unique within a project.
        /// </summary>
        public static Observation Create(PipelineCore pipeline, Frame frame, string name, string url,
                                         string cameraModel, bool useForReconstruction,
                                         int width, int height, int bands, int bits, int day, int version, int index,
                                         bool save = true)
        {
            Observation obs = new Observation(frame, name, url, cameraModel, useForReconstruction,
                                              width, height, bands, bits, day, version, index);
            if (save)
            {
                obs.Save(pipeline);
            }
            return obs;
        }

        /// <summary>
        /// Save this observation without overwriting any values it may be missing
        /// </summary>
        public virtual void Save(PipelineCore pipeline)
        {
            IsValid();
            pipeline.SaveDatabaseItem(this);
        }

        /// <summary>
        /// Finds an observation based on its name and project
        /// Return null if observation cannot be found
        /// </summary>
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

        public bool IsLinear()
        {
            return ((CameraModel)JsonHelper.FromJson(CameraModel)).Linear;
        }

        public bool CheckLinear(bool linear)
        {
            return linear == IsLinear();
        }

        public bool CheckLinear(RoverProductGeometry geometry)
        {
            switch (geometry)
            {
                case RoverProductGeometry.Linearized: return IsLinear();
                case RoverProductGeometry.Raw: return !IsLinear();
                default: return false;
            }
        }

        public virtual string ToString(bool brief)
        {
            var cm = (CameraModel)JsonHelper.FromJson(CameraModel);
            return string.Format("{0} Frame={1}, {2}{3}CameraModel={4} ({5}), {6}Size={7}x{8}, Bands={9}, " +
                                 "Bits={10}, Day={11}{12}",
                                 Name, FrameName,
                                 brief ? "" : string.Format("Url={0}, ", Url),
                                 brief ? "" : string.Format("Project={0}, ", ProjectName),
                                 cm.GetType().Name,
                                 cm.Linear ? "linear" : "nonlinear",
                                 brief ? "" : string.Format("ForReconstruction={0}, ", UseForReconstruction),
                                 Width, Height, Bands, Bits, Day,
                                 brief ? "" : string.Format(", FeaturesGuid={0}", FeaturesGuid),
                                 brief ? "" : string.Format(", BlendedGuid={0}", BlendedGuid));
        }

        public override string ToString()
        {
            return ToString(brief: false);
        }
    }
}
