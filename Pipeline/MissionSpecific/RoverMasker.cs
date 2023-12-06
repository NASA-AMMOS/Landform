using Microsoft.Xna.Framework;
using JPLOPS.Geometry;
using JPLOPS.Imaging;
using JPLOPS.RayTrace;

namespace JPLOPS.Pipeline
{
    /// <summary>
    /// Articulation parameters for a rover pose. All angles are in radians.
    /// </summary>
    public abstract class RoverArticulation
    {
    }
    
    public class MSLRoverArticulation : RoverArticulation
    {
        public double LeftRockerAngle;
        public double LeftBogieAngle;
        public double RightBogieAngle;
        public double RightRockerAngle { get { return -LeftRockerAngle; } }
        public double ArmAngle1;
        public double ArmAngle2;
        public double ArmAngle3;
        public double ArmAngle4;
        public double ArmAngle5;
        public double MastAzimuth;
        public double MastElevation;
    }

    public interface RoverModel
    {
        Mesh BuildMesh(RoverArticulation pose, bool includeBody = true);
    }

    public abstract class RoverMasker
    {
        protected readonly MissionSpecific mission;

        public RoverMasker(MissionSpecific mission)
        {
            this.mission = mission;
        }

        /// <summary>
        /// Get an instance of the mission specific rover model.
        /// Or, return null if there is no rover model, and then no pixels will be rover masked.
        /// </summary>
        public abstract RoverModel GetRoverModel();

        public abstract PDSRoverArticulationParser GetParser(PDSMetadata metadata);

        /// <summary>
        /// build a rover mask binary image which is 0 for masked pixels
        /// </summary>
        public Image Build(PDSMetadata metadata)
        {
            return Build(metadata, new PDSParser(metadata));
        }

        /// <summary>
        /// build a rover mask binary image which is 0 for masked pixels
        /// all the other Build() and LoadOrBuild() APIs funnel to this one, which can be overriden
        /// </summary>
        public virtual Image Build(PDSMetadata metadata, PDSParser parser)
        {
            Image res = new Image(1, metadata.Width, metadata.Height);

            var rover = GetRoverModel();
            var articulation = GetParser(metadata).Parse();
            if (rover != null && articulation != null)
            {
                var posedRover = rover.BuildMesh(articulation, !mission.IsHazcam(mission.GetCamera(parser)));

                //coarse test to see if rover is in frame at all (raycasts are expensive)
                ConvexHull roverHull = ConvexHull.Create(posedRover);
                ConvexHull obsHull = ConvexHull.FromParams(metadata.CameraModel, metadata.Width, metadata.Height);
                if (!obsHull.Intersects(roverHull))
                {
                    for (int i = 0; i < res.Width; i++)
                    {
                        for (int j = 0; j < res.Height; j++)
                        {
                            res[0, j, i] = 1;
                        }
                    }
                }
                else
                {
                    var sc = new SceneCaster();
                    sc.AddMesh(posedRover, null, Matrix.Identity);
                    sc.Build();

                    for (int i = 0; i < res.Width; i++)
                    {
                        for (int j = 0; j < res.Height; j++)
                        {
                            var ray = metadata.CameraModel.Unproject(new Vector2(i, j));
                            res[0, j, i] = sc.Occludes(ray) ? 0 : 1;
                        }
                    }
                }
            }
            else //no rover model or no articulation => no masked pixels
            {
                for (int i = 0; i < res.Width; i++)
                {
                    for (int j = 0; j < res.Height; j++)
                    {
                        res[0, j, i] = 1;
                    }
                }
            }

            return res;
        }

        /// <summary>
        /// load a rover mask binary image which is 0 for masked pixels
        /// </summary>
        public Image Load(PipelineCore pipeline, string maskUrl)
        {
            //see comments in ImageMasker.GetOrCreateMask() regarding noCache
            var mask = new Image(pipeline.LoadImage(maskUrl, noCache: true));
            mask.ApplyInPlace(v => v == 0 ? 1.0f : 0.0f);
            return mask;
        } 

        public Image LoadOrBuild(PipelineCore pipeline, string maskUrl, PDSMetadata metadata)
        {
            if (!string.IsNullOrEmpty(maskUrl))
            {
                var mask = Load(pipeline, maskUrl);
                if (mask.Width == metadata.Width && mask.Height == metadata.Height)
                {
                    return mask;
                }
                else
                {
                    pipeline.LogWarn("rover mask {0} is {1}x{2} but observation image is {3}x{4}, " +
                                     "attempting to build synthetic mask", maskUrl, mask.Width, mask.Height,
                                     metadata.Width, metadata.Height);
                }
            }
            return Build(metadata);
        }

        public virtual int GetBorderPixels(PDSParser parser)
        {
            return ImageMasker.DEF_MASK_BORDER;
        }

        public virtual bool CanMakeSyntheticRoverMasks()
        {
            return false;
        }
    }

    public class MSLRoverMasker : RoverMasker
    {
        public MSLRoverMasker(MissionMSL mission) : base(mission) { }

        public override RoverModel GetRoverModel() { return null; }

        public override int GetBorderPixels(PDSParser parser)
        {
            var cam = mission.GetCamera(parser);
            if (mission.IsHazcam(cam))
            {
                //enough to fix some errors in our homemade rover masks (rear: RTG, front: arm parts)
                //ISSUE 1082 for example
                return 150;
            }
            else
            {
                return base.GetBorderPixels(parser);
            }
        }
      
        public override PDSRoverArticulationParser GetParser(PDSMetadata metadata)
        {
            return new MSLRoverArticulationParser(metadata);
        }
    }

    public class M2020RoverMasker : RoverMasker
    {
        public M2020RoverMasker(MissionM2020 mission) : base(mission) { }

        public override RoverModel GetRoverModel() { return null; }

        public override int GetBorderPixels(PDSParser parser)
        {
            //M20 camera SIS says zcam
            //"can acquire images of up to 1648 x 1200 pixels (generally only 1600 x 1200 are used)"
            //and during early mission at least we are seeing black borders on the left and right sides of zcam
            //images, but those images are also 1648 wide
            int def = base.GetBorderPixels(parser);
            var cam = mission.GetCamera(parser);
            if (mission.IsMastcam(cam) && parser.metadata.Width > 1600)
            {
                return def + (parser.metadata.Width - 1600) / 2;
            }
            return def;
        }

        public override PDSRoverArticulationParser GetParser(PDSMetadata metadata)
        {
            return new M2020RoverArticulationParser(metadata);
        }

        public override bool CanMakeSyntheticRoverMasks()
        {
            return false;
        }
    }
}
