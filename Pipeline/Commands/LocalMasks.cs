using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommandLine;
using log4net;
using OPS.Util;
using OPS.Pipeline.AlignmentServer;

namespace OPS.Pipeline
{
    [Verb("local-masks", HelpText = "create image masks locally")]
    public class LocalMasksOptions : PipelineCoreOptions
    {
        [Value(0, Required = true, HelpText = "project name", Default = null)]
        public string ProjectName { get; set; }

        [Option(HelpText = "Recreate masks that already exist", Default = false)]
        public bool RedoMasks { get; set; }

        [Option(HelpText = "Show progress", Default = false)]
        public bool NoProgress { get; set; }
    }

    public class LocalMasks : LocalPipeline
    {
        private LocalMasksOptions options;

        public LocalMasks(LocalMasksOptions options) : base(options)
        {
            this.options = options;
        }

        public int Run()
        {
            var project = Project.Find(this, options.ProjectName);

            if (project == null)
            {
                LogError("project \"{0}\" not found", options.ProjectName);
                return 1;
            }

            var observations = RoverObservation.Find(this, options.ProjectName)
                .Where(o => o.ObservationType == ObservationType.Image.ToString())
                .Where(o => o.UseForReconstruction)
                .ToList();
            int no = observations.Count;

            LogInfo("computing masks for {0} reconstruction images", no);

            double startSec = UTCTime.Now();
            int nc = 0, ne = 0, nm = 0, np = 0;
            Parallel.ForEach(observations, obs => {
                    if (obs.MaskGuid != null && obs.MaskGuid != Guid.Empty)
                    {
                        Interlocked.Increment(ref ne);
                        if (!options.RedoMasks)
                        {
                            LogVerbose("not recomputing mask for observation {0}", obs.Name);
                            return;
                        }
                        else
                        {
                            LogVerbose("recomputing mask for observation {0}", obs.Name);
                        }
                    }
                    else
                    {
                        LogVerbose("computing mask for observation {0}", obs.Name);
                    }
                    Interlocked.Increment(ref nm);
                    Interlocked.Increment(ref np);
                    if (!options.NoProgress)
                    {
                        LogInfo("computing {0} masks in parallel, completed {1}/{2}", np, nc, no);
                    }
                    var mask = new PngDataProduct(RoverMask.Build(LoadImage(obs.Url)));
                    SaveDataProduct(project.ProductPath, mask, project.Name);
                    obs.MaskGuid = mask.Guid;
                    obs.Save(this);
                    Interlocked.Decrement(ref np);
                    Interlocked.Increment(ref nc);
                });
            double totalSec = UTCTime.Now() - startSec;

            LogInfo("processed {0} reconstruction images ({1:F3}s), computed {2} masks ({3} existing)",
                    nc, totalSec, nm, ne);

            return 0;
        }
    }
}
