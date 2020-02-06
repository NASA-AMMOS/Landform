using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using CommandLine;
using OPS.Util;
using OPS.Cloud;
using OPS.Pipeline;
using OPS.Imaging;
using OPS.Geometry;
using OPS.Pipeline.AlignmentServer;

namespace OPS.Landform
{
    [Verb("process-contextual", HelpText = "process contextual meshes")]
    public class ProcessContextualOptions : LandformServiceOptions
    {
        [Option(Required = false, Default = null, HelpText = "Output directory or S3 folder, if unset use same folder as input")]
        public override string OutputFolder { get; set; }

        [Option(Required = false, Default = null, HelpText = "Input directory or S3 folder with sol replaced with #####, optional with --service")]
        public string RDRDir { get; set; }

        [Option(Required = false, Default = null, HelpText = "Sol(s) and range(s) with primary one first, e.g. 8,6-10")]
        public string Sols { get; set; }

        [Option(Required = false, Default = null, HelpText = "Sitedrives with primary one first")]
        public string SiteDrives { get; set; }

        [Option(Required = false, Default = false, HelpText = "Don't fetch")]
        public bool NoFetch { get; set; }

        [Option(Required = false, Default = false, HelpText = "Don't ingest")]
        public bool NoIngest { get; set; }

        [Option(Required = false, Default = false, HelpText = "Don't generate tileset")]
        public bool NoTileset { get; set; }

        [Option(Required = false, Default = false, HelpText = "Don't write/update combined scene manifest on s3")]
        public bool NoCombinedManifest { get; set; }

        [Option(Required = false, Default = false, HelpText = "Don't recursively search for RDRs")]
        public bool NoRecursiveSearch { get; set; }

        [Option(Required = false, Default = true, HelpText = "option disabled for this command")]
        public override bool RecursiveSearch { get; set; }

        [Option(Required = false, Default = false, HelpText = "option disabled for this command")]
        public override bool CaseSensitiveSearch { get; set; }

        [Value(0, Required = false, Default = null, HelpText = "option disabled for this command")]
        public override string ProjectName { get; set; }

        [Option(Required = false, Default = null, HelpText = "option disabled for this command")]
        public override string MeshFormat { get; set; }

        [Option(Required = false, Default = null, HelpText = "option disabled for this command")]
        public override string ImageFormat { get; set; }
    }

    public class ProcessContextual : LandformService
    {
        protected ProcessContextualOptions options;

        private class GenericContextualMeshMessage : QueueMessage
        {
#pragma warning disable 0649
            public string rdrDir; //e.g. s3://BUCKET/ods/g64/sol/#####/ids/rdr; if null or empty then use options.RDRDir
            public int primarySol;
            public string sols; //e.g. 2,3,4-9,14; if null or empty then use primarySol
            public string primarySiteDrive;
            public string siteDrives; //e.g. 0230001,0230002,0240001; if null or empty then use primarySiteDrive
#pragma warning restore 0649
        }

        public ProcessContextual(ProcessContextualOptions options) : base(options)
        {
            this.options = options;
        }

        protected override void RunBatch()
        {
            RunPhase("build contextual tileset ",
                     () => BuildContextualTileset(MakeParameters(options.RDRDir, options.Sols, options.SiteDrives)));
        }

        protected override string GetDefaultQueueName()
        {
            return mission.GetContextualMeshQueueName();
        }

        protected override string GetDefaultFailQueueName()
        {
            return mission.GetContextualMeshFailQueueName();
        }

        protected override int GetMaxHandlerSec()
        {
            return options.MaxHandlerSec > 0 ? options.MaxHandlerSec : mission.GetContextualMeshQueueMaxHandlerSec();
        }

        protected override int GetMaxMessageAgeSec()
        {
            return options.MaxMessageAgeSec > 0 ? options.MaxMessageAgeSec :
                mission.GetContextualMeshQueueMessageMaxAgeSec();
        }

        private ContextualMeshParameters GetParameters(QueueMessage msg)
        {
            return options.UseGenericMessageType ? MakeParameters((GenericContextualMeshMessage)msg, options.RDRDir) :
                mission.GetParametersFromContextualMeshQueueMessage(msg);
        }

        protected override string DescribeMessage(QueueMessage msg)
        {
            ContextualMeshParameters parameters = null;
            try
            {
                parameters = GetParameters(msg);
            }
            catch {} //ignore
            return "contextual mesh " + (parameters != null ? parameters.TilesetName : "(unknown)");
        }

        protected override QueueMessage DequeueOneMessage(MessageQueue queue)
        {
            return options.UseGenericMessageType ?
                messageQueue.DequeueOne<GenericContextualMeshMessage>() :
                mission.DequeueContextualMeshMessage(queue);
        }

        protected override QueueMessage ParseMessage(string json)
        {
            return options.UseGenericMessageType ?
                JsonHelper.FromJson<GenericContextualMeshMessage>(json, autoTypes: false) :
                mission.ParseContextualMeshQueueMessage(json);
        }

        protected override bool AcceptMessage(QueueMessage msg)
        {
            try
            {
                return GetParameters(msg) != null; 
            }
            catch (Exception ex)
            {
                pipeline.LogWarn(ex.Message);
                return false;
            }
        }

        protected override bool HandleMessage(QueueMessage msg)
        {
            var parameters = GetParameters(msg);

            if (parameters == null)
            {
                return true; //mission decided to ignore this message, remove it from the queue
            }

            BuildContextualTileset(parameters); //throws exception on error or if killed

            return true; //successfully processed, remove message from queue
        }

        protected override bool ParseArguments()
        {
            options.RecursiveSearch = !options.NoRecursiveSearch;

            if (!base.ParseArguments())
            {
                return false; //e.g. --help
            }

            if (messageQueue == null)
            {
                if (string.IsNullOrEmpty(options.RDRDir) ||
                    string.IsNullOrEmpty(options.Sols) || string.IsNullOrEmpty(options.SiteDrives))
                {
                    throw new Exception("--rdrdir, --sols, and --sitedrives required without --service");
                }
            }
            else if (!string.IsNullOrEmpty(options.Sols) || !string.IsNullOrEmpty(options.SiteDrives))
            {
                throw new Exception("cannot combine --sols or --sitedrives with --service");
            }

            return true;
        }

        protected override Project GetProject()
        {
            return null;
        }

        protected override string GetLogFilePrefix()
        {
            return "log-Landform-process-contextual";
        }

        protected override string GetConfigSuffix()
        {
            return "-contextual";
        }

        protected override string GetCacheDir()
        {
            return "contextual";
        }

        private string GetSolRanges(HashSet<int> sols)
        {
            var ranges = new List<int[]>();
            foreach (var sol in sols.OrderBy(sol => sol))
            {
                if (ranges.Count == 0 || ranges[ranges.Count - 1][1] != sol - 1)
                {
                    ranges.Add(new int[] { sol, sol });
                }
                else
                {
                    ranges[ranges.Count - 1][1] = sol;
                }
            }
            return String.Join(",", ranges.Select(range => range[0] + (range[0] != range[1] ? ("-" + range[1]) : "")));
        }

        private ContextualMeshParameters MakeParameters(GenericContextualMeshMessage msg, string defaultRDRDir)
        {
            var ret = new ContextualMeshParameters();

            ret.RDRDir = msg.rdrDir ?? defaultRDRDir;
            
            ret.PrimarySol = msg.primarySol;
            ret.Sols.Add(ret.PrimarySol);
            
            if (!string.IsNullOrEmpty(msg.sols))
            {
                ret.Sols.UnionWith(FetchData.ExpandSolSpecifier(msg.sols).Select(sol => int.Parse(sol)));
            }
            
            ret.PrimarySiteDrive = new SiteDrive(msg.primarySiteDrive);
            ret.SiteDrives.Add(ret.PrimarySiteDrive);
            
            if (!string.IsNullOrEmpty(msg.siteDrives))
            {
                ret.SiteDrives.UnionWith(SiteDrive.ParseList(msg.siteDrives));
            }

            return ret;
        }

        private ContextualMeshParameters MakeParameters(string rdrDir, string sols, string siteDrives)
        {
            var ret = new ContextualMeshParameters();
            ret.RDRDir = rdrDir;
            int sep = Math.Max(sols.Length, Math.Min(sols.IndexOf('-'), sols.IndexOf(',')));
            ret.PrimarySol = int.Parse(sols.Substring(0, sep));
            ret.Sols.UnionWith(FetchData.ExpandSolSpecifier(sols).Select(sol => int.Parse(sol)));
            var sds = SiteDrive.ParseList(siteDrives);
            ret.PrimarySiteDrive = sds[0];
            ret.SiteDrives.UnionWith(sds);
            return ret;
        }

        private void BuildContextualTileset(ContextualMeshParameters p)
        {
            BuildContextualTileset(p.RDRDir, p.PrimarySol, p.Sols, p.PrimarySiteDrive, p.SiteDrives);
        }

        /// <summary>
        /// rdrDir is e.g.
        /// * "s3://BUCKET/ods/g64/sol/#####/ids/rdr"
        /// * "s3://BUCKET/foo/bar"
        /// * "c:/foo/bar"
        /// * "./foo/bar"
        /// * null -> use options.RDRDir
        /// </summary>
        private void BuildContextualTileset(string rdrDir,
                                            int primarySol, HashSet<int> sols,
                                            SiteDrive primarySiteDrive, HashSet<SiteDrive> siteDrives)
        {
            if (!sols.Contains(primarySol))
            {
                throw new ArgumentException("sols must contain primarySol");
            }

            if (!siteDrives.Contains(primarySiteDrive))
            {
                throw new ArgumentException("siteDrives must contain primarySiteDrive");
            }

            rdrDir = rdrDir ?? options.RDRDir;
            if (String.IsNullOrEmpty(rdrDir))
            {
                throw new ArgumentException("rdrDir empty");
            }
            rdrDir = StringHelper.NormalizeUrl(rdrDir, preserveTrailingSlash: false);

            string missionStr = mission.GetMission().ToString();
            string sdStr = primarySiteDrive.ToString();
            string solStr = StringHelper.FixedWidthInt(FetchData.SOL_WILDCARD, primarySol);
            string sdsStr = string.Join(",", siteDrives.ToArray());
            string project = string.Format("{0}_{1}", solStr, sdStr);
            string venue = string.Format("contextual_{0}_{1}", missionStr, project);
            string venueDir = storageDir + "/" + venue;
            string solDir = StringHelper.ReplaceFixedWidthIntWildcard(rdrDir, FetchData.SOL_WILDCARD, primarySol);
            string ingestDir = solDir;
            string tilesetDir = GetTilesetDir(venue, sdStr);
            string destDir = GetDestDir(solDir);

            pipeline.LogInfo("building contextual tileset {0} from {1} sitedrives in {2} sols",
                             project, siteDrives.Count, sols.Count);
            try
            {
                Cleanup(venueDir);

                Configure(venue);

                if (!options.NoFetch && rdrDir.StartsWith("s3://") && !(pipeline is CloudPipeline))
                {
                    ingestDir = string.Format("{0}/{1}/{2}", storageDir, venue, RDR_SUBDIR);
                    string fetchSols = GetSolRanges(sols);
                    RunCommand("fetch", fetchSols, ingestDir, rdrDir, "--mission", missionStr, "--summary",
                               "--onlyforsitedrives", sdsStr, "--awsprofile", awsProfile, "--awsregion", awsRegion);
                }

                if (!options.NoIngest)
                {
                    if (sols.Count > 1 && ingestDir.StartsWith("s3://") && ingestDir == solDir && ingestDir != rdrDir)
                    {
                        throw new NotImplementedException("s3 reference ingestion from multiple sols not implemented");
                    }
                    
                    RunCommand("ingest", project, "--mission", missionStr, "--onlyforsitedrives", sdsStr,
                               "--inputpath", ingestDir + "/" + (options.RecursiveSearch ? "**" : "*"));
                }

                if (!options.NoTileset)
                {
                    RunCommand("bev-align", project, "--fixsitedrives", sdStr); //TODO check mesh formats
                    
                    RunCommand("build-geometry", project, "--meshframe", sdStr);
                    
                    RunCommand("build-tiling-input", project, "--meshframe", sdStr);
                    
                    RunCommand("blend-images", project, "--meshframe", sdStr);
                    
                    RunCommand("build-tileset", project, "--meshframe", sdStr);
                    
                    RunCommand("update-scene-manifest", project, "--notactical", "--nourls",
                               "--sol", solStr, "--sitedrive", sdStr, "--manifestfile", tilesetDir + "/" + SCENE_JSON);

                    SaveTileset(tilesetDir, project, destDir);
                }

                if (!options.NoCombinedManifest)
                {
                    RunCommand("update-scene-manifest", project, "--tilesetdir", destDir,
                               "--rdrdir", rdrDir, "--sol", solStr, "--sitedrive", sdStr,
                               "--awsprofile", awsProfile, "--awsregion", awsRegion);
                }

                Cleanup(venueDir);
            }
            catch
            {
                Cleanup(venueDir);
                throw;
            }
        }
    }
}
