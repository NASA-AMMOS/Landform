using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Amazon.DynamoDBv2.DataModel;
using OPS.Util;
using OPS.Cloud;
using OPS.Imaging;

namespace OPS.Pipeline.AlignmentServer
{
    public enum TextureVariant { Original, Blurred, Blended };

    /// <summary>
    /// Represents an image or 3D shape measurement of the environment
    /// Can be connected to Frames and aligned with other observations through FrameTransforms
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

        public bool UseForAlignment;

        public bool UseForMeshing;

        public bool UseForTexturing;

        public int Width;

        public int Height;

        public int Bands;

        public int Bits;

        public int Day;

        public int Version;

        public int Index;

        [DynamoDBIgnore]
        [JsonIgnore]
        private string _cameraModel;

        public string CameraModel {
            get
            {
                return _cameraModel;
            }
            set
            {
                _cameraModel = value;
                _linear = null;
            }
        }

        [DynamoDBIgnore]
        [JsonIgnore]
        private bool? _linear;

        [DynamoDBIgnore]
        [JsonIgnore]
        public bool IsLinear
        {
            get
            {
                if (!_linear.HasValue)
                {
                    _linear = ((CameraModel)JsonHelper.FromJson(CameraModel)).Linear;
                }
                return _linear.Value;
            }
        }

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
        protected Observation(Frame frame, string name, string url, CameraModel cameraModel,
                              bool useForAlignment, bool useForMeshing, bool useForTexturing,
                              int width, int height, int bands, int bits, int day, int version, int index)
        {
            this.ProjectName = frame.ProjectName;
            this.FrameName = frame.Name;
            this.Name = name;
            this.Url = url;
            this.MaskGuid = Guid.Empty;
            this.FeaturesGuid = Guid.Empty;
            this.BlurredGuid = Guid.Empty;
            this.BlendedGuid = Guid.Empty;
            this.CameraModel = JsonHelper.ToJson(cameraModel);
            this.UseForAlignment = useForAlignment;
            this.UseForMeshing = useForMeshing;
            this.UseForTexturing = useForTexturing;
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
        public static Observation
            Create(PipelineCore pipeline, Frame frame, string name, string url, CameraModel cameraModel,
                   bool useForAlignment, bool useForMeshing, bool useForTexturing,
                   int width, int height, int bands, int bits, int day, int version, int index,
                   bool save = true)
        {
            Observation obs = new Observation(frame, name, url, cameraModel,
                                              useForAlignment, useForMeshing, useForTexturing,
                                              width, height, bands, bits, day, version, index);
            if (save)
            {
                obs.Save(pipeline);
            }
            return obs;
        }

        public virtual void Save(PipelineCore pipeline)
        {
            IsValid();
            pipeline.SaveDatabaseItem(this);
        }

        public virtual void Delete(PipelineCore pipeline, bool ignoreErrors = true)
        {
            pipeline.DeleteDatabaseItem(this, ignoreErrors);
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

        public bool CheckLinear(bool linear)
        {
            return linear == IsLinear;
        }

        public bool CheckLinear(RoverProductGeometry geometry)
        {
            switch (geometry)
            {
                case RoverProductGeometry.Linearized: return IsLinear;
                case RoverProductGeometry.Raw: return !IsLinear;
                default: return false;
            }
        }

        public virtual string ToString(bool brief)
        {
            var cm = (CameraModel)JsonHelper.FromJson(CameraModel);
            return string.Format("{0} Frame={1}, {2}{3}CameraModel={4} ({5}), {6}{7}{8}Size={9}x{10}, Bands={9}, " +
                                 "Bits={12}, Day={13}, Version={14}, Index={15}",
                                 Name, FrameName, //0, 1
                                 brief ? "" : string.Format("Url={0}, ", Url), //2
                                 brief ? "" : string.Format("Project={0}, ", ProjectName), //3
                                 cm.GetType().Name, //4
                                 cm.Linear ? "linear" : "nonlinear", //5
                                 brief ? "" : string.Format("UseForAlignment={0}, ", UseForAlignment), //6
                                 brief ? "" : string.Format("UseForMeshing={0}, ", UseForMeshing), //7
                                 brief ? "" : string.Format("UseForTexturing={0}, ", UseForTexturing), //8
                                 Width, Height, Bands, Bits, Day, Version, Index); //9-15
        }

        public override string ToString()
        {
            return ToString(brief: false);
        }
    }
}
