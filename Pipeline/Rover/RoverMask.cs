using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using OPS.Util;
using OPS.Imaging;
using OPS.RayTrace;
using OPS.Pipeline.AlignmentServer;

namespace OPS.Pipeline
{
    public class RoverMask
    {
        public static CuriosityRoverModel RoverModel = new CuriosityRoverModel();

        /// <summary>
        /// build a rover mask binary image which is 0 for masked pixels
        /// returns null if image does not have PDS metadata
        /// </summary>
        public static Image Build(Image image)
        {
            return Build(image.Metadata);
        }

        /// <summary>
        /// build a rover mask binary image which is 0 for masked pixels
        /// </summary>
        public static Image Build(ImageMetadata metadata)
        {
            return metadata is PDSMetadata ? Build(metadata as PDSMetadata) : null;
        }

        /// <summary>
        /// build a rover mask binary image which is 0 for masked pixels
        /// </summary>
        public static Image Build(PDSMetadata metadata)
        {
            return Build(metadata, new PDSParser(metadata));
        }

        /// <summary>
        /// build a rover mask binary image which is 0 for masked pixels
        /// </summary>
        public static Image Build(PDSMetadata metadata, PDSParser parser )
        {
            var posedRover = RoverModel.BuildMesh(parser.Articulation, !MissionMSL.IsHazcam(parser.Camera));

            var sc = new SceneCaster();
            sc.AddMesh(posedRover, null, Matrix.Identity);
            sc.Build();

            Image res = new Image(1, metadata.Width, metadata.Height);
            for (int i = 0; i < res.Width; i++)
            {
                for (int j = 0; j < res.Height; j++)
                {
                    var ray = metadata.CameraModel.Unproject(new Vector2(i, j));
                    res[0, j, i] = sc.Occludes(ray) ? 0 : 1;
                }
            }

            return res;
        }

        /// <summary>
        /// load or build a rover mask binary image which is 0 for masked pixels
        /// uses mask from maskObs if available and size matches imageObs
        /// otherwise builds from imageObs, but returns null if imageObs does not have PDS metadata
        /// </summary>
        public static Image LoadOrBuild(PipelineCore pipeline, Observation maskObs, Observation imageObs,
                                        bool clone = false)
        {
            return LoadOrBuild(pipeline, maskObs, pipeline.LoadImage(imageObs.Url), imageObs.Name, clone);
        }

        /// <summary>
        /// load or build a rover mask binary image which is 0 for masked pixels
        /// uses mask from maskObs if available and size matches refImage
        /// otherwise builds from refImage, but returns null if refImage does not have PDS metadata
        /// </summary>
        public static Image LoadOrBuild(PipelineCore pipeline, Observation maskObs, Image refImage,
                                        string observationName, bool clone = false)
        {
            if (maskObs != null)
            {
                if (maskObs.Width == refImage.Width && maskObs.Height == refImage.Height)
                {
                    var mask = pipeline.LoadImage(maskObs.Url);
                    return clone ? new Image(mask) : mask;
                } 
                else
                {
                    pipeline.LogWarn("not using rover mask product for observation {0}, mismatched image size",
                                     observationName);
                }
            }
            return Build(refImage, observationName, pipeline);
        }

        /// <summary>
        /// load or build a rover mask binary image which is 0 for masked pixels
        /// uses mask from maskUrl if available and size matches refImage
        /// otherwise builds from refImage, but returns null if refImage does not have PDS metadata
        /// </summary>
        public static Image LoadOrBuild(PipelineCore pipeline, string maskUrl, Image refImage, string observationName,
                                        bool clone = false)
        {
            if (!string.IsNullOrEmpty(maskUrl))
            {
                var mask = pipeline.LoadImage(maskUrl);
                if (mask.Width == refImage.Width && mask.Height == refImage.Height)
                {
                    return clone ? new Image(mask) : mask;
                } 
                else
                {
                    pipeline.LogWarn("not using rover mask product for observation {0}, mismatched image size",
                                     observationName);
                }
            }
            return Build(refImage, observationName, pipeline);
        }

        /// <summary>
        /// build a rover mask binary image which is 0 for masked pixels
        /// returns null if image does not have PDS metadata
        /// </summary>
        public static Image Build(Image image, string observationName, ILogger logger = null)
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
    }
}
