using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Diagnostics;
using CommandLine;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using OPS.Util;
using OPS.Imaging;
using OPS.Geometry;
using OPS.Pipeline;
using OPS.Pipeline.AlignmentServer;
using OPS.Pipeline.TilingServer;

namespace OPS.Landform
{
    public class GeometryCommandOptions : WedgeCommandOptions
    {
        [Option(HelpText = "Scene mesh coordinate frame: numeric sitedrive SSSSSDDDDD, root, newest, or oldest", Default = "newest")]
        public string MeshFrame { get; set; }
    }

    public class GeometryCommand : WedgeCommand
    {
        protected new GeometryCommandOptions options;

        protected string meshFrame;

        public GeometryCommand(GeometryCommandOptions options) : base(options)
        {
            this.options = options;
        }

        protected override bool ParseArgumentsAndLoadCaches(string outDir, ObservationType[] obsTypes = null,
                                                           bool onlyObsForReconstruction = false)
        {
            meshFrame = options.MeshFrame.ToLower();
            bool specificSiteDrive = false;
            if (meshFrame != "newest" && meshFrame != "oldest")
            {
                FrameTransform.ParseFrameName(ref meshFrame, out specificSiteDrive);
                if (!specificSiteDrive && meshFrame != "root")
                {
                    throw new Exception("unsupported mesh frame: " + meshFrame);
                }
            }

            outDir = string.Format("{0}/{1}Frame", outDir, meshFrame);

            if (!base.ParseArgumentsAndLoadCaches(outDir, obsTypes, onlyObsForReconstruction))
            {
                return false; //help
            }

            if (meshFrame != "newest" && meshFrame != "oldest")
            {
                if (siteDrives.Length == 0)
                {
                    throw new Exception("no sitedrives");
                }

                if (meshFrame == "newest")
                {
                    meshFrame = siteDrives.OrderByDescending(sd => sd).First().ToString();
                }
                else
                {
                    meshFrame = siteDrives.OrderBy(sd => sd).First().ToString();
                }

                specificSiteDrive = true;
            }

            if (specificSiteDrive && !frameCache.ContainsFrame(meshFrame))
            {
                throw new Exception("sitedrive output frame not found: " + meshFrame);
            }

            pipeline.LogInfo("scene mesh frame: {0}", meshFrame);

            return true;
        }
    }
}
