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
        /// returns null if image does not have PDS metadata
        /// </summary>
        public Image Build(Image image)
        {
            return Build(image.Metadata);
        }

        /// <summary>
        /// build a rover mask binary image which is 0 for masked pixels
        /// </summary>
        public Image Build(ImageMetadata metadata)
        {
            return metadata is PDSMetadata ? Build(metadata as PDSMetadata) : null;
        }

        /// <summary>
        /// build a rover mask binary image which is 0 for masked pixels
        /// returns null if image does not have PDS metadata
        /// </summary>
        public Image Build(Image image, string observationName, ILogger logger = null)
        {
            {
                if (!(image.Metadata is PDSMetadata))
                {
                    if (logger != null)
                        logger.LogWarn("no rover mask product available for observation {0} " +
                                       "and cannot generate a synthetic rover mask because metadata is not PDS",
                                       observationName);
                    return null;
                }
                if (logger != null)
                {
                    logger.LogVerbose("generating synthetic rover mask for {0}", observationName);
                }
                return Build(image.Metadata as PDSMetadata);
            }
        }

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
                var posedRover = rover.BuildMesh(articulation, !mission.IsHazcam(parser.Camera));
                
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
        /// load or build a rover mask binary image which is 0 for masked pixels
        /// uses mask from maskObs if available and size matches imageObs
        /// otherwise builds from imageObs, but returns null if imageObs does not have PDS metadata
        /// </summary>
        public Image LoadOrBuild(PipelineCore pipeline, Observation maskObs, Observation imageObs, bool clone = false)
        {
            return LoadOrBuild(pipeline, maskObs, pipeline.LoadImage(imageObs.Url), imageObs.Name, clone);
        }

        /// <summary>
        /// load or build a rover mask binary image which is 0 for masked pixels
        /// uses mask from maskObs if available and size matches refImage
        /// otherwise builds from refImage, but returns null if refImage does not have PDS metadata
        /// </summary>
        public Image LoadOrBuild(PipelineCore pipeline, Observation maskObs, Image refImage, string observationName,
                                 bool clone = false)
        {
            if (maskObs != null)
            {
                if (maskObs.Width == refImage.Width && maskObs.Height == refImage.Height)
                {
                    try
                    {
                        var mask = pipeline.LoadImage(maskObs.Url);
                        return clone ? new Image(mask) : mask;
                    }
                    catch (Exception ex)
                    {
                        pipeline.LogWarn("error loading rover mask {0}, generating: {1}", maskObs.Url, ex.Message);
                    }
                } 
                else
                {
                    pipeline.LogWarn("not using rover mask {0}, mismatched image size {1}x{2}, generating",
                                     maskObs.Url, maskObs.Width, maskObs.Height);
                }
            }
            return Build(refImage, observationName, pipeline);
        }

        /// <summary>
        /// load or build a rover mask binary image which is 0 for masked pixels
        /// uses mask from maskUrl if available and size matches refImage
        /// otherwise builds from refImage, but returns null if refImage does not have PDS metadata
        /// </summary>
        public Image LoadOrBuild(PipelineCore pipeline, string maskUrl, Image refImage, string observationName,
                                 bool clone = false)
        {
            if (!string.IsNullOrEmpty(maskUrl))
            {
                try
                {
                    var mask = pipeline.LoadImage(maskUrl);
                    if (mask.Width == refImage.Width && mask.Height == refImage.Height)
                    {
                        return clone ? new Image(mask) : mask;
                    } 
                    else
                    {
                        pipeline.LogWarn("not using rover mask {0}, mismatched image size {1}x{2}, generating",
                                         maskUrl, mask.Width, mask.Height);
                    }
                }
                catch (Exception ex)
                {
                    pipeline.LogWarn("error loading rover mask {0}, generating: {1}", maskUrl, ex.Message);
                }
            }
            return Build(refImage, observationName, pipeline);
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
