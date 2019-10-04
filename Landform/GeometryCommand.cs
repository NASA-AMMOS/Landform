using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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
        [Option(HelpText = "Scene mesh coordinate frame: auto, passthrough, newest, oldest, mission_root, project_root, numeric sitedrive SSSSSDDDDD", Default = "auto")]
        public virtual string MeshFrame { get; set; }

        [Option(HelpText = "Use level of detail meshes provided in input mesh instead of decimation", Default = false)]
        public bool LoadLODs { get; set; }
    }

    public class GeometryCommand : WedgeCommand
    {
        protected GeometryCommandOptions gcopts;

        protected string meshFrame;

        protected Mesh mesh;
        protected List<Mesh> meshLODs;  //the full set of levels of detail for the mesh with the first being highest quality and the last being worst quality
        protected SceneMesh sceneMesh;

        public GeometryCommand(GeometryCommandOptions gcopts) : base(gcopts)
        {
            this.gcopts = gcopts;
        }

        protected override bool ParseArgumentsAndLoadCaches(string outDir, ObservationType[] obsTypes = null,
                                                            bool onlyObsForReconstruction = false)
        {
            //pass null as outDir because we'll be setting it ourselves below
            if (!base.ParseArgumentsAndLoadCaches(null, obsTypes, onlyObsForReconstruction))
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
            meshFrame = GetMeshFrame().ToLower().Trim();

            string missionRoot = mission.RootFrameName();

            var specials =
                new string[] { "auto", "passthrough", "newest", "oldest", "mission_root", "project_root", missionRoot };

            bool isSiteDrive = (new Regex("\\d{10}")).IsMatch(meshFrame);
            bool isSpecial = !isSiteDrive && specials.Contains(meshFrame);

            if (!isSiteDrive && !isSpecial)
            {
                throw new Exception("unsupported mesh frame: " + meshFrame);
            }

            if (meshFrame == "auto")
            {
                meshFrame = GetAutoMeshFrame();
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
                if (string.IsNullOrEmpty(effectiveRootFrame))
                {
                    //this can happen if there were no frames to load or the frame cache was not loaded
                    throw new Exception("mission root output requested but no frames or frame cache not loaded");
                }

                if (effectiveRootFrame != missionRoot)
                {
                    throw new Exception(string.Format("mission root output {0} requested but effective root is {1}",
                                                      missionRoot, effectiveRootFrame));
                }
                meshFrame = missionRoot;
            }
            else if (meshFrame == "project_root")
            {
                if (string.IsNullOrEmpty(effectiveRootFrame))
                {
                    //this can happen if there were no frames to load or the frame cache was not loaded
                    throw new Exception("project root output requested but effective root unknown");
                }
                meshFrame = effectiveRootFrame;
            }
            else if (meshFrame == "newest" || meshFrame == "oldest")
            {
                if (observationCache == null)
                {
                    throw new Exception("observation cache not loaded, cannot resolve special frame: " + meshFrame);
                }
                                              
                var sds = observationCache
                    .GetAllObservations()
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

            if (isSiteDrive)
            {
                if (frameCache == null)
                {
                    throw new Exception("frame cache not loaded, cannot resolve frame: " + meshFrame);
                }
                if (!frameCache.ContainsFrame(meshFrame))
                {
                    throw new Exception("sitedrive frame not found: " + meshFrame);
                }
            }

            pipeline.LogInfo("scene mesh frame: {0}{1}", meshFrame,
                             origMeshFrame != meshFrame ? " (" + origMeshFrame + ")" : "");
        }
    }
}
