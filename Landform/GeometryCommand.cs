using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Diagnostics;
using CommandLine;
using OPS.Util;
using OPS.Geometry;
using OPS.Pipeline.AlignmentServer;
using OPS.Pipeline;

namespace OPS.Landform
{
    public class GeometryCommandOptions : WedgeCommandOptions
    {
        [Option(HelpText = "Scene mesh coordinate frame: auto, passthrough, newest, oldest, mission_root, project_root, numeric sitedrive SSSSSDDDDD", Default = "auto")]
        public virtual string MeshFrame { get; set; }
    }

    public class GeometryCommand : WedgeCommand
    {
        protected GeometryCommandOptions gcopts;

        protected string meshFrame;

        public GeometryCommand(GeometryCommandOptions gcopts) : base(gcopts)
        {
            this.gcopts = gcopts;
        }

        protected override bool ParseArgumentsAndLoadCaches(string outDir)
        {
            //pass null as outDir because we'll be setting it ourselves below
            if (!base.ParseArgumentsAndLoadCaches(null))
            {
                return false; //help
            }

            HandleSpecialMeshFrames();

            SetOutDir(DecorateOutDir(outDir));

            return true;
        }

        protected override string DecorateOutDir(string outDir)
        {
            return base.DecorateOutDir(string.Format("{0}/{1}Frame", outDir, meshFrame));
        }

        protected virtual string GetMeshFrame()
        {
            return gcopts.MeshFrame;
        }

        protected virtual string GetAutoMeshFrame()
        {
            return "newest";
        }

        protected virtual bool PassthroughMeshFrameAllowed()
        {
            return false;
        }

        protected virtual bool NonPassthroughMeshFrameAllowed()
        {
            return true;
        }

        private void HandleSpecialMeshFrames()
        {
            meshFrame = GetMeshFrame();

            if (string.IsNullOrEmpty(meshFrame))
            {
                return;
            }

            meshFrame = meshFrame.ToLower().Trim();

            if (meshFrame == "auto")
            {
                meshFrame = GetAutoMeshFrame();
            }
                
            string missionRoot = mission != null ? mission.RootFrameName() : null;

            var specials =
                new string[] { "passthrough", "newest", "oldest", "mission_root", "project_root", missionRoot };

            bool isSiteDrive = SiteDrive.IsSiteDriveString(meshFrame);
            bool isSpecial = !isSiteDrive && specials.Contains(meshFrame);

            if (!isSiteDrive && !isSpecial)
            {
                throw new Exception("unsupported mesh frame: " + meshFrame);
            }

            var origMeshFrame = meshFrame;
            if (meshFrame == "passthrough")
            {
                if (!PassthroughMeshFrameAllowed())
                {
                    throw new Exception("--meshframe=passthrough not allowed");
                }
            }
            else if (!NonPassthroughMeshFrameAllowed())
            {
                throw new Exception("only --meshframe=passthrough allowed");
            }

            if (meshFrame == "mission_root" || meshFrame == missionRoot)
            {
                meshFrame = "root"; //recognized as a meta-name by FrameCache.GetObservationTransform()
            }
            else if (meshFrame == "project_root")
            {
                if (rootSiteDrive == null)
                {
                    //this can happen if there were no frames to load or the frame cache was not loaded
                    throw new Exception("project root output requested but no root site drive");
                }
                if (rootSiteDrive == mission.GetLandingSiteDrive())
                {
                    meshFrame = "root";
                }
                else
                {
                    meshFrame = rootSiteDrive.ToString();
                }
            }
            else if (meshFrame == "newest" || meshFrame == "oldest")
            {
                if (observationCache == null)
                {
                    throw new Exception("observation cache not loaded, cannot resolve special frame: " + meshFrame);
                }
                                              
                var sds = observationCache
                    .GetAllObservations()
                    .Where(obs => obs is RoverObservation)
                    .Select(obs => ((RoverObservation)obs).SiteDrive)
                    .Distinct()
                    .ToArray();

                if (sds.Length == 0)
                {
                    throw new Exception("no sitedrives");
                }

                if (meshFrame == "newest")
                {
                    meshFrame = sds.OrderByDescending(sd => sd).First().ToString();
                }
                else
                {
                    meshFrame = sds.OrderBy(sd => sd).First().ToString();
                }

                isSiteDrive = true;
            }

            //some workflows do not load frame cache, for example updating scene manifest for tactical meshes
            if (isSiteDrive && frameCache != null && !frameCache.ContainsFrame(meshFrame))
            {
                throw new Exception("sitedrive frame not found: " + meshFrame);
            }

            pipeline.LogInfo("scene mesh frame: {0}{1}", meshFrame,
                             origMeshFrame != meshFrame ? " (" + origMeshFrame + ")" : "");
        }

        protected string CheckOutputURL<T>(string url, string defaultFilename, string outDir,
                                           SerializerMap<T> serializerMap = null)
        {
            url = StringHelper.NormalizeUrl(url);
            var ext = StringHelper.GetUrlExtension(url);
            if (serializerMap != null && serializerMap.CheckFormat(ext) == null)
            {
                throw new Exception("unsupported output format " + ext);
            }
            if (url.StartsWith("."))
            {
                url = defaultFilename + url;
            }
            if (pipeline is CloudPipeline)
            {
                if (!url.Contains("://"))
                {
                    url = pipeline.GetStorageUrl(outDir, project.Name, url);
                }
                else if (!url.StartsWith(pipeline.StorageUrlWithVenue))
                {
                    throw new Exception(string.Format("output URL {0} outside cloud storage area", url));
                }
            }
            return url;
        }
    }
}
