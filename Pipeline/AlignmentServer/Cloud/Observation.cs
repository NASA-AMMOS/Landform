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
    public enum ObservationType
    {
        Image,
        Range,
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

        public Guid FeaturesGuid;

        public string FrameName;

        public string ObservationType;

        public string CameraModel;

        //it might be nice to have this field which would be a RoverProductGeometry string (e.g. Linarized, Raw)
        //but that would introduce a redundancy with CameraModel.Linear, so avoiding for now
        //public string Geometry;

        public bool UseForReconstruction;

        public int Width;

        public int Height;

        public int Bands;

        public int Bits;

        public int Day;

        public int Index;

        //DEPRECATED - for legacy compat only
        public string MaskGuid;

        //DEPRECATED - for legacy compat only
        public string FeatureUrl;

        /// Add required fields here 
        protected void IsValid()
        {
            if (!(Url != null && FrameName != null && ProjectName != null && Name != null && ObservationType != null))
            {
                throw new Exception("Missing required property in Observation");
            }
        }

        private static Dictionary<RoverProductType, ObservationType> productTypeMap =
            new Dictionary<RoverProductType, ObservationType>();

        static Observation()
        {
            productTypeMap[RoverProductType.Image] = OPS.Pipeline.AlignmentServer.ObservationType.Image;
            productTypeMap[RoverProductType.Range] = OPS.Pipeline.AlignmentServer.ObservationType.Range;
            productTypeMap[RoverProductType.XYZ] = OPS.Pipeline.AlignmentServer.ObservationType.Points;
            productTypeMap[RoverProductType.NormalMap] = OPS.Pipeline.AlignmentServer.ObservationType.Normals;
            productTypeMap[RoverProductType.RoverMask] = OPS.Pipeline.AlignmentServer.ObservationType.RoverMask;
        }

        public static ObservationType ProductTypeToObservationType(RoverProductType productType)
        {
            return productTypeMap[productType];
        }

        public static bool AllowedProductType(RoverProductType productType)
        {
            return productTypeMap.ContainsKey(productType);
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
        protected Observation(Frame frame, string name, string url, string observationType, string cameraModel,
                              bool useForReconstruction, int width, int height, int bands, int bits, int day, int index)
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
            this.Bands = bands;
            this.Bits = bits;
            this.Day = day;
            this.Index = index;
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
        public static Observation Create(PipelineCore pipeline, Frame frame, string name, string url,
                                         string observationType, string cameraModel, bool useForReconstruction,
                                         int width, int height, int bands, int bits, int day, int index)
        {
            Observation obs = new Observation(frame, name, url, observationType, cameraModel, useForReconstruction,
                                              width, height, bands, bits, day, index);
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
            return string.Format("{0} Frame={1}, {2}{3}Type={4}, CameraModel={5} ({6}), {7}Size={8}x{9}, Bands={10}, " +
                                 "Bits={11}, Day={12}{13}",
                                 Name, FrameName,
                                 brief ? "" : string.Format("Url={0}, ", Url),
                                 brief ? "" : string.Format("Project={0}, ", ProjectName),
                                 ObservationType,
                                 cm.GetType().Name,
                                 cm.Linear ? "linear" : "nonlinear",
                                 brief ? "" : string.Format("ForReconstruction={0}, ", UseForReconstruction),
                                 Width, Height, Bands, Bits, Day,
                                 brief ? "" : string.Format(", FeaturesGuid={0}", FeaturesGuid));
        }

        public override string ToString()
        {
            return ToString(brief: false);
        }
    }
}
