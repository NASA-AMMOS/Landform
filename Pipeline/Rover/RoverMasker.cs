using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using OPS.Util;
using OPS.Geometry;
using OPS.Imaging;
using OPS.RayTrace;
using OPS.Pipeline.AlignmentServer;

namespace OPS.Pipeline
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
        private readonly MissionSpecific mission;

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
                var posedRover = rover.BuildMesh(articulation, !mission.IsHazcam(mission.GetRoverProductCamera(parser.InstrumentId)));

                //coarse test to see if rover is in frame at all (raycasts are expensive)
                ConvexHull roverHull = new ConvexHull(posedRover);
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
        public Image Load(PipelineCore pipeline, string maskUrl, bool clone = false)
        {
            var mask = pipeline.LoadImage(maskUrl);
            return clone ? new Image(mask) : mask;
        } 

        public Image LoadOrBuild(PipelineCore pipeline, string maskUrl, PDSMetadata metadata, bool clone = false)
        {
            if (!string.IsNullOrEmpty(maskUrl))
            {
                return Load(pipeline, maskUrl, clone);
            }
            else
            {
                return Build(metadata);
            }
        }
    }

    public class MSLRoverMasker : RoverMasker
    {
        private static CuriosityRoverModel roverModel = new CuriosityRoverModel();

        public MSLRoverMasker(MissionMSL mission) : base(mission) { }

        public override RoverModel GetRoverModel() { return roverModel; }

        public override PDSRoverArticulationParser GetParser(PDSMetadata metadata)
        {
            return new MSLRoverArticulationParser(metadata);
        }
    }

    public class M2020RoverMasker : RoverMasker
    {
        public M2020RoverMasker(MissionM2020 mission) : base(mission) { }

        //TODO https://github.jpl.nasa.gov/OnSight/Landform/issues/554
        public override RoverModel GetRoverModel() { return null; }

        public override PDSRoverArticulationParser GetParser(PDSMetadata metadata)
        {
            return new M2020RoverArticulationParser(metadata);
        }
    }
}
