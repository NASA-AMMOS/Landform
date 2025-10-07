using System;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using JPLOPS.Imaging;
using JPLOPS.Util;

namespace JPLOPS.Pipeline.AlignmentServer
{
    public enum TextureVariant { Original, Stretched, Blurred, Blended, SkyBlended };

    /// <summary>
    /// Represents an image or 3D shape measurement of the environment
    /// Can be connected to Frames and aligned with other observations through FrameTransforms
    /// </summary>
    public class Observation : IURLFileSet
    {
        //not a valid observation index
        //used to mark texels where backproject was not possible because there is no corresponding point on the mesh
        //this can happen, for example, when the mesh is atlassed with more than one chart
        //and there is a "gutter" between charts
        //this is different than a texel which does map to the mesh but where backproject failed e.g. due to occlusion
        //for that see NO_OBSERVATION_INDEX
        public const int GUTTER_INDEX = 0;

        //used to mark pixels in the backproject index that do correspond to points on the mesh
        //(i.e. not pixels in the texture atlas gutter, for that see GUTTER_INDEX)
        //but that did not successfully backproject to any observation
        //for example this can occur for hole-filled portions of the mesh that are occluded in all observations
        //mainly this can happen when orbital texturing is not available
        //but it can happen inside "caves" even when orbital is available
        public const int NO_OBSERVATION_INDEX = 1;

        //minimum valid observation index
        //(in legacy TerrainTools index 65535 and 0 are equivalently treated as "no observation"
        //and those values can get serialized out to the index image for pixels where backprojection failed)
        public const int MIN_INDEX = 2;

        //limit indices to 16 bit
        //because we use 16 bit PPM for overlay index products
        public const int MAX_INDEX = 65535;

        //limit indices to unsigned ints that can be exactly represented in a float
        //https://stackoverflow.com/a/3793950
        //this makes it possible to store an observation index in one band of a float image
        //and we want to do that when creating backproject index images
        //public const int MAX_INDEX = 16777216;

        public const int ORBITAL_IMAGE_INDEX = MAX_INDEX; 
        public const int ORBITAL_DEM_INDEX = ORBITAL_IMAGE_INDEX - 1;

        [DBRangeKey]
        public string ProjectName;

        [DBHashKey]
        public string Name; //rover product ID

        public string Url; //PDS or VICAR image with metadata that loads as PDSMetadata

        //for Url, case sensitive, without leading dots
        public HashSet<string> AlternateExtensions = new HashSet<string>(); //MT safety: lock before accessing

        public Guid MaskGuid; //combines rover mask, user mask, invalid/missing pixels, and border

        public Guid HullGuid; //camera model frustum hull

        public Guid FeaturesGuid;

        public Guid StretchedGuid;

        public Guid BlurredGuid;

        public Guid BlendedGuid;

        public Guid SkyBlendedGuid;

        public Guid StatsGuid;

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

        public double HullFarClip;

        [JsonConverter(typeof(CameraModelConverter))]
        public CameraModel CameraModel;

        [JsonIgnore]
        public bool IsLinear { get { return CameraModel.Linear; } }

        [JsonIgnore]
        public bool IsOrbitalImage { get { return Index == ORBITAL_IMAGE_INDEX; } }

        [JsonIgnore]
        public bool IsOrbitalDEM { get { return Index == ORBITAL_DEM_INDEX; } }

        [JsonIgnore]
        public bool IsOrbital { get { return IsOrbitalDEM || IsOrbitalImage; } }

        /// Add required fields here 
        protected void IsValid()
        {
            if (!(Url != null && FrameName != null && ProjectName != null && Name != null))
            {
                throw new Exception("Missing required property in Observation");
            }
        }

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
            this.HullGuid = Guid.Empty;
            this.FeaturesGuid = Guid.Empty;
            this.StretchedGuid = Guid.Empty;
            this.BlurredGuid = Guid.Empty;
            this.BlendedGuid = Guid.Empty;
            this.SkyBlendedGuid = Guid.Empty;
            this.StatsGuid = Guid.Empty;
            this.CameraModel = cameraModel;
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
                case RoverProductGeometry.Any: return true;
                default: return false;
            }
        }

        public string GetUrlWithExtension(string ext)
        {
            ext = ext.TrimStart('.');
            if (Url.EndsWith("." + ext, StringComparison.OrdinalIgnoreCase))
            {
                return Url;
            }
            string actualExt = AlternateExtensions
                .Where(ex => ex.Equals(ext, StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();
            if (actualExt == null)
            {
                throw new Exception(string.Format("no ext {0} in observation {1}, available: {2}",
                                                  ext, Name, string.Join(", ", AlternateExtensions)));
            }
            return StringHelper.StripUrlExtension(Url) + "." + actualExt;
        }
        
        public bool HasUrlExtension(string ext)
        {
            ext = ext.TrimStart('.');
            return Url.EndsWith("." + ext, StringComparison.OrdinalIgnoreCase) ||
                AlternateExtensions.Any(ex => ex.Equals(ext, StringComparison.OrdinalIgnoreCase));
        }

        public IEnumerable<string> GetUrlExtensions()
        {
            yield return StringHelper.GetUrlExtension(Url).TrimStart('.');
            foreach (string ext in AlternateExtensions)
            {
                yield return ext;
            }
        }

        public TextureVariant GetTextureVariantWithFallback(TextureVariant variant)
        {
            switch (variant)
            {
                case TextureVariant.Original: return TextureVariant.Original;
                case TextureVariant.Stretched:
                {
                    return StretchedGuid != Guid.Empty ? TextureVariant.Stretched : TextureVariant.Original;
                }
                case TextureVariant.Blurred:
                {
                    return
                        BlurredGuid != Guid.Empty ? TextureVariant.Blurred :
                        StretchedGuid != Guid.Empty ? TextureVariant.Stretched :
                        TextureVariant.Original;
                }
                case TextureVariant.Blended:
                {
                    return
                        BlendedGuid != Guid.Empty ? TextureVariant.Blended :
                        StretchedGuid != Guid.Empty ? TextureVariant.Stretched :
                        TextureVariant.Original;
                }
                case TextureVariant.SkyBlended:
                {
                    return
                        SkyBlendedGuid != Guid.Empty ? TextureVariant.SkyBlended :
                        StretchedGuid != Guid.Empty ? TextureVariant.Stretched :
                        TextureVariant.Original;
                }
                default: throw new Exception("unknown texture variant: " + variant);
            }
        }

        public Guid GetTextureVariantGuid(TextureVariant variant)
        {
            switch (variant)
            {
                case TextureVariant.Stretched: return StretchedGuid;
                case TextureVariant.Blurred: return BlurredGuid;
                case TextureVariant.Blended: return BlendedGuid;
                case TextureVariant.SkyBlended: return SkyBlendedGuid;
                default: throw new Exception("unsupported texture variant: " + variant); //including Original
            }
        }

        public void SetTextureVariantGuid(TextureVariant variant, Guid guid)
        {
            switch (variant)
            {
                case TextureVariant.Stretched: StretchedGuid = guid; break;
                case TextureVariant.Blurred: BlurredGuid = guid; break;
                case TextureVariant.Blended: BlendedGuid = guid; break;
                case TextureVariant.SkyBlended: SkyBlendedGuid = guid; break;
                default: throw new Exception("unsupported texture variant: " + variant); //including Original
            }
        }

        public virtual string ToString(bool brief)
        {
            return string.Format("{0} Frame={1}, {2}{3}CameraModel={4} ({5}), {6}{7}{8}Size={9}x{10}, Bands={11}, " +
                                 "Bits={12}, Day={13}, Version={14}, Index={15}{16}",
                                 Name, FrameName, //0, 1
                                 brief ? "" : string.Format("Url={0}, ", Url), //2
                                 brief ? "" : string.Format("Project={0}, ", ProjectName), //3
                                 CameraModel.GetType().Name, //4
                                 IsLinear ? "linear" : "nonlinear", //5
                                 brief ? "" : string.Format("UseForAlignment={0}, ", UseForAlignment), //6
                                 brief ? "" : string.Format("UseForMeshing={0}, ", UseForMeshing), //7
                                 brief ? "" : string.Format("UseForTexturing={0}, ", UseForTexturing), //8
                                 Width, Height, Bands, Bits, Day, Version, Index, //9-15
                                 (brief || !IsOrbital) ? "" : IsOrbitalImage ? " (orbital image)" : " (orbital DEM)");
        }

        public override string ToString()
        {
            return ToString(brief: false);
        }
    }
}
