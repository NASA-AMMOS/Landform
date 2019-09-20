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
        public virtual string MeshFrame { get; set; }
    }

    public class GeometryCommand : WedgeCommand
    {
        protected GeometryCommandOptions gcopts;

        protected string meshFrame;

        protected Mesh mesh;
        protected SceneMesh sceneMesh;

        public GeometryCommand(GeometryCommandOptions gcopts) : base(gcopts)
        {
            this.gcopts = gcopts;
        }

        protected virtual string GetMeshFrame()
        {
            return gcopts.MeshFrame;
        }

        protected override bool ParseArgumentsAndLoadCaches(string outDir, ObservationType[] obsTypes = null,
                                                            bool onlyObsForReconstruction = false)
        {
            meshFrame = GetMeshFrame().ToLower();

            bool specificSiteDrive = false;
            if (meshFrame != "newest" && meshFrame != "oldest")
            {
                FrameTransform.ParseFrameName(ref meshFrame, out specificSiteDrive);
                if (!specificSiteDrive && meshFrame != "root")
                {
                    throw new Exception("unsupported mesh frame: " + meshFrame);
                }
            }

            if (!base.ParseArgumentsAndLoadCaches(null, obsTypes, onlyObsForReconstruction))
            {
                return false; //help
            }

            HandleSpecialMeshFrames(specificSiteDrive);

            outDir = string.Format("{0}/{1}Frame", outDir, meshFrame);
            outDir = FrameTransform.AppendSourcesPath(outDir, adjustedSources, priorSources, gcopts.UsePriors);
            SetOutDir(outDir);

            return true;
        }

        private void HandleSpecialMeshFrames(bool specificSiteDrive)
        {
            var origMeshFrame = meshFrame;
            if (meshFrame == "root")
            {
                string missionRoot = mission.RootFrameName();
                if (effectiveRootFrame != missionRoot)
                {
                    throw new Exception(string.Format("mission root output \"{0}\" requested but effective root is {1}",
                                                      missionRoot, effectiveRootFrame));
                }
                meshFrame = missionRoot;
            }
            else if (meshFrame == "newest" || meshFrame == "oldest")
            {
                if (observationCache == null)
                {
                    throw new Exception(string.Format("cannot resolve {0} sitedrive frame: no observations", meshFrame));
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

                specificSiteDrive = true;
            }

            if (specificSiteDrive && frameCache != null && !frameCache.ContainsFrame(meshFrame))
            {
                throw new Exception("sitedrive output frame not found: " + meshFrame);
            }

            pipeline.LogInfo("scene mesh frame: {0}{1}", meshFrame,
                             origMeshFrame != meshFrame ? " (" + origMeshFrame + ")" : "");
        }
    }
}
