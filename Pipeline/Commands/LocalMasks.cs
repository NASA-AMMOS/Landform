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
    }

    public class LocalMasks : LocalPipeline
    {
        private static readonly ILog logger = LogManager.GetLogger(typeof(LocalIngest));

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

            int no = 0, nio = 0, ne = 0, nm = 0, np = 0;
            double startSec = UTCTime.Now();
            Parallel.ForEach(RoverObservation.Find(this, options.ProjectName), obs => {
                    Interlocked.Increment(ref no);
                    if (obs.ObservationType == ObservationType.Image.ToString())
                    {
                        Interlocked.Increment(ref nio);
                        if (obs.MaskGuid != null && obs.MaskGuid != Guid.Empty)
                        {
                            Interlocked.Increment(ref ne);
                            if (!options.RedoMasks)
                            {
                                LogInfo("not recomputing mask for observation {0}", obs.Name);
                                return;
                            }
                            else
                            {
                                LogInfo("recomputing mask for observation {0}", obs.Name);
                            }
                        }
                        else
                        {
                            LogInfo("computing mask for observation {0}", obs.Name);
                        }
                        Interlocked.Increment(ref nm);
                        Interlocked.Increment(ref np);
                        LogVerbose("computing {0} masks in parallel", np);
                        var mask = new PngDataProduct(RoverMask.Build(LoadImage(obs.Url)));
                        SaveDataProduct(project.ProductPath, mask, project.Name);
                        obs.MaskGuid = mask.Guid;
                        obs.Save(this);
                        Interlocked.Decrement(ref np);
                        
                    }
                });
            double totalSec = UTCTime.Now() - startSec;

            
            LogInfo("processed {0} observations in {1:F3} sec, {2} images, {3} had existing masks, computed {4} masks",
                    no, totalSec, nio, ne, nm);

            return 0;
        }
    }
}
