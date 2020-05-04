using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CommandLine;
using OPS.Util;
using OPS.Cloud;
using OPS.Pipeline;
using OPS.Imaging;
using OPS.Geometry;
using OPS.Pipeline.AlignmentServer;

/// <summary>
/// Landform contextual mesh tileset workflow service and tool.
///
/// Automates the contextual mesh tileset workflow:
///
/// 0. fetch
/// 1. ingest
/// 2. bev-align
/// 3. heightmap-align
/// 4. build-geometry
/// 5. build-tiling-input
/// 6. blend-images
/// 7. build-tileset
/// 8. update-scene-manifest (manifest just for the contextual mesh tileset with relative URLs)
/// 9. update-scene-manifest (optional combined manifest for the scene with abolute URLs)
///
/// As a service, process-contextual is designed to run over a long period of time, receiving messages on an SQS queue,
/// creating contextual meshes, and uploading them back to S3.
///
/// As a command line tool, process-contextual can be used to build individual contextual mesh tilesets.  It can either
/// operate entirely locally, reading from and writing to disk, or it can read from and write to S3.
///
/// Also see Scripts/process-contextual.sh, which has overlapping functionality for the batch-mode case.
/// (process-contextual.sh does not implement the service case.)  process-contextual.sh is intended for use by
/// developers only, and has additional options for development and debugging workflows.  process-contextual
/// (ProcessContextual.cs) can be used by developers but is mainly intended for deployment and production use.
///
/// Also see ProcessTactical.cs and processTactical.sh which automate the tactical mesh tileset workflow.
///
/// A contextual mesh is generated for a specific primary sol and primary sitedrive.  It combines data from a set of
/// sols and sitedrives (which must contain the primary sol/sitedrive).  When run as a command line tool these are given
/// by the --sols and --sitedrives options, where the first listed sol and sitedrive are primary.  When run as a service
/// they are included in the SQS messages.
///
/// RDRs are fetched (or in the case of local files, read from disk) from a specified directory, recursively by default.
/// If operating on multiple sols the RDR directory can contain a ##### wildcard which will be replaced with each sol
/// number.
///
/// Example RDR directory specifiers:
/// * "s3://BUCKET/ods/g64/sol/#####/ids/rdr"
/// * "s3://BUCKET/foo/bar"
/// * "c:/foo/bar"
/// * "./foo/bar"
///
/// The output tileset is named TTTT_SSSDDDD where TTTT is the primary sol and SSSDDDD is the primary sitedrive.  It is
/// written to rdrDir/tileset/TTTT_SSSDDDD (*), unless --outputfolder is specified, in which case it is written to a
/// subdirectory TTTT_SSSDDDD there. (*) actually if rdrDir contains a prefix ending /rdr then the output directory is
/// that prefix but with rdr replaced with rdr/tileset/TTTT_SSSDDDD.
///
/// When run as a service the RDR directory is also given as part of each SQS message.  Thus, the service will write the
/// tilesets back to the same RDR tree as the source RDRs, but under the rdr/tileset subdirectory.
///
/// The tileset will contain
/// * one .b3dm file per tile
/// * a tilest file TTTT_SSSDDDD/TTTT_SSSDDDD_tileset.json
/// * a manifest file TTTT_SSSDDDD/TTTT_SSSDDDD_scene.json with relative URLs
/// * a stats file TTTT_SSSDDDD/TTTT_SSSDDDD_stats.txt.
///
/// A combined scene manifest with absolute URLs can also be optionally created or updated as a sibling of the output
/// tileset directory.  In that case the update-scene-manifest tool will also include any sibling tactical mesh tilesets
/// in the manifest.
///
/// Can also run in master service mode by specifying --master.  In that mode the service listens for messages
/// indicating XYZ list files have been created or updated.  There is expected to be one list file per sitedrive,
/// listing the XZY RDRs available for it.  The master the scans for other sitedrive list files and uses them to
/// determine one or more contextual mesh messages based on various parameters which limit the minimum size of a
/// sitedrive for which a contextual mesh is built, the maximum range of sols for which to include adjacent sitedrives,
/// the maximum distance for adjacent sitedrives, and the maximum number of XYZ observations to include in a contextual
/// mesh.  Uses PlacesDB to get the distance between sitedrive origins; if PlacesDB is not available then contextual
/// meshes will only be built for single sitedrives. (Note PlacesDB is also required to include orbital in contextual
/// meshes.)
///
/// Run as service:
///
/// Landform.exe process-contextual --service --mission=M2020 \
///    --queuename=landform-contextual --failqueuename=landform-contextual-fail
///
/// Run as master service:
///
/// Landform.exe process-contextual --master --mission=M2020 \
///    --queuename=landform-contextual-master --failqueuename=landform-contextual-master-fail \
///    --workerqueuename=landform-contextual
///
/// Windjana in batch mode using already downloaded RDRs:
///
/// Landform.exe process-contextual --mission=M2020 --rdrdir=../rdrs --sols=0609-0630
///   --sitedrives=0311472,0311256,0311444,0311330 --nocombinedmanifest
///
/// </summary>
namespace OPS.Landform
{
    [Verb("process-contextual", HelpText = "process contextual meshes")]
    public class ProcessContextualOptions : LandformServiceOptions
    {
        [Option(Required = false, Default = null, HelpText = "Output directory or S3 folder, if unset use same folder as input")]
        public override string OutputFolder { get; set; }

        [Option(Required = false, Default = null, HelpText = "Input directory or S3 folder with sol replaced with #####, optional with --service")]
        public string RDRDir { get; set; }

        [Option(Required = false, Default = null, HelpText = "Sol(s) and range(s) with primary one first, e.g. 8,6-10, mutually exclusive with --service")]
        public string Sols { get; set; }

        [Option(Required = false, Default = null, HelpText = "Sitedrives with primary one first, mutually exclusive with --service")]
        public string SiteDrives { get; set; }

        [Option(Required = false, Default = false, HelpText = "Don't fetch")]
        public bool NoFetch { get; set; }

        [Option(Required = false, Default = null, HelpText = "Persistent download dir, defaults to \"fetched\" subdir of local Landform storage dir")]
        public string FetchDir { get; set; }

        [Option(Required = false, Default = null, HelpText = "Max fetched RDR bytes on disk, not including orbital, integer with optional case-insensitive suffix K,M,G, no limit if omitted or non-positive")]
        public string MaxFetch { get; set; }

        [Option(Required = false, Default = null, HelpText = "Max fetched orbital bytes on disk, integer with optional case-insensitive suffix K,M,G, no limit if empty or non-positive")]
        public string MaxOrbital { get; set; }

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

        [Option(Required = false, Default = null, HelpText = "Override default orbital DEM file path")]
        public string OrbitalDEM { get; set; }

        [Option(Required = false, Default = null, HelpText = "Override default orbital DEM URL")]
        public string OrbitalDEMURL { get; set; }

        [Option(Required = false, Default = null, HelpText = "Override default orbital image file path")]
        public string OrbitalImage { get; set; }

        [Option(Required = false, Default = null, HelpText = "Override default orbital image URL")]
        public string OrbitalImageURL { get; set; }

        [Option(HelpText = "Abort contextual mesh workflow on unexpected error in an alignment stage", Default = false)]
        public bool AbortOnAlignmentError { get; set; }

        [Option(HelpText = "Run as contextual mesh master service", Default = false)]
        public bool Master { get; set; }

        [Option(Required = false, Default = "lis", HelpText = "Master service list filename extension")]
        public string ListFormat { get; set; }

        [Option(Required = false, Default = "xyz_", HelpText = "Master service list filename prefix")]
        public string ListPrefix { get; set; }

        [Option(Required = false, Default = null, HelpText = "Worker message queue name, required with --master")]
        public string WorkerQueueName { get; set; }

        [Option(Required = false, Default = ProcessContextual.DEF_DEBOUNCE_SEC, HelpText = "Master waits at least this long after any list file changed for a given RDR directory before firing a new contextual mesh message, default if negative")]
        public int MasterDebounceSec { get; set; }

        [Option(Required = false, Default = false, HelpText = "Worker message queue is Landform owned")]
        public bool LandformOwnedWorkerQueue { get; set; }

        [Option(Required = false, Default = 4, HelpText = "Minimum number of wedges for primary site drive in a contextual mesh, non-positive for no limit")]
        public int MinPrimarySiteDriveWedges { get; set; }

        [Option(Required = false, Default = 1, HelpText = "Minimum number of wedges for a site drive to be included in a contextual mesh, non-positive for no limit")]
        public int MinSiteDriveWedges { get; set; }

        [Option(Required = false, Default = 100, HelpText = "Maximum number of wedges for which to build a contextual mesh, non-positive for no limit")]
        public int MaxContextualMeshWedges { get; set; }

        [Option(Required = false, Default = 10, HelpText = "Max number of site drives to include in contextual mesh, non-positive for no limit")]
        public int MaxSiteDrives{ get; set; }

        [Option(Required = false, Default = 32, HelpText = "Max distance in meters from origin of a site drive to origin of primary site drive to include in contextual mesh, non-positive for no limit")]
        public double MaxSiteDriveDistance { get; set; }

        [Option(Required = false, Default = ProcessContextual.DEF_MAX_SOL_RANGE, HelpText = "Max difference between sol and primary sol to include in contextual mesh, negative to use default")]
        public int MaxSolRange { get; set; }
    }

    public class ProcessContextual : LandformService
    {
        public const string FETCH_DIR = "fetched";

        public const int DEF_MAX_HANDLER_SEC = 2 * 60 * 60; //2 hours
        public const int DEF_MAX_MESSAGE_AGE_SEC = 6 * 60 * 60; //6 hours

        public const int DEF_MASTER_MAX_HANDLER_SEC = 10 * 60; //10 minutes
        public const int DEF_MASTER_MAX_MESSAGE_AGE_SEC = 1 * 60 * 60; //1 hour

        public const int MASTER_LOOP_PERIOD_SEC = 10;

        public const int DEF_DEBOUNCE_SEC = 60;

        public const int DEF_MAX_SOL_RANGE = 30;

        protected ProcessContextualOptions options;

        private string listExt;

        private MessageQueue workerQueue;

        //message sent from master to worker
        //defines the job of building one contextual mesh
        //equality is based only on rdrDir, primarySol, primarySiteDrive
        private class ContextualMeshMessage : QueueMessage
        {
            //designed for serialization to JSON so using camelCase not StudlyCaps
#pragma warning disable 0649
            public string rdrDir; //e.g. s3://BUCKET/ods/g64/sol/#####/ids/rdr; if null or empty then use options.RDRDir
            public int primarySol;
            public string sols; //e.g. 2,3,4-9,14; if null or empty then use primarySol
            public string primarySiteDrive;
            public string siteDrives; //e.g. 0230001,0230002,0240001; if null or empty then use primarySiteDrive
            public int numWedges = -1; //used only for information and sorting, negative if unknown
#pragma warning restore 0649

            public override int GetHashCode()
            {
                return HashCombiner.Combine(rdrDir.GetHashCode(),
                                            HashCombiner.Combine(primarySol, primarySiteDrive.GetHashCode()));
            }

            public override bool Equals(object obj)
            {
                if (!(obj is ContextualMeshMessage))
                {
                    return false;
                }
                var msg = obj as ContextualMeshMessage;
                return msg.rdrDir == rdrDir && msg.primarySol == primarySol && msg.primarySiteDrive == primarySiteDrive;
            }
        }

        //defines the job of building one contextual mesh
        //can be built from a ContextualMeshMessage (worker service mode) or command line arguments (batch mode)
        private class ContextualMeshParameters
        {
            public string RDRDir;
            public int PrimarySol;
            public HashSet<int> Sols = new HashSet<int>();
            public SiteDrive PrimarySiteDrive;
            public HashSet<SiteDrive> SiteDrives = new HashSet<SiteDrive>();
            public string TilesetName { get { return string.Format("{0:D4}_{1}", PrimarySol, PrimarySiteDrive); } }
        }

        //for testing master instead of SNS wrapped S3 ObjectCreated messages
        private class GenericContextualMasterMessage : QueueMessage
        {
#pragma warning disable 0649
            public string listUrl;
#pragma warning restore 0649
        }

        //in-memory list file database for master mode: list file directory -> list file URL -> list file
        //written by ProcessListFile(), read by MasterLoop()
        private ConcurrentDictionary<string, Stamped<ConcurrentDictionary<string, Stamped<ListFile>>>> listDirs =
            new ConcurrentDictionary<string, Stamped<ConcurrentDictionary<string, Stamped<ListFile>>>>();

        //rdrDir -> timestamp (ms since UTC) when master last made pass over it 
        //confined to the master loop thread
        private Dictionary<string, long> lastMasterPass = new Dictionary<string, long>();

        public ProcessContextual(ProcessContextualOptions options) : base(options)
        {
            this.options = options;
        }

        protected override void RunBatch()
        {
            RunPhase("build contextual tileset ",
                     () => BuildContextualTileset(MakeParameters(options.RDRDir, options.Sols, options.SiteDrives)));
        }

        protected override int GetMaxHandlerSec()
        {
            return options.MaxHandlerSec > 0 ? options.MaxHandlerSec
                : options.Master ? DEF_MASTER_MAX_HANDLER_SEC : DEF_MAX_HANDLER_SEC;
        }

        protected override int GetMaxMessageAgeSec()
        {
            return options.MaxMessageAgeSec > 0 ? options.MaxMessageAgeSec
                : options.Master ? DEF_MASTER_MAX_MESSAGE_AGE_SEC : DEF_MAX_MESSAGE_AGE_SEC;
        }

        protected override string DescribeMessage(QueueMessage msg, bool verbose = false)
        {
            if ((msg is GenericContextualMasterMessage) || (msg is SNSMessageWrapper))
            {
                string url = null;
                try
                {
                    url = GetUrlFromMessage(msg);
                }
                catch {} //ignore
                return "xyz list file " + (url ?? "(unknown)");
            }
            else if (msg is ContextualMeshMessage)
            {
                ContextualMeshParameters parameters = null;
                try
                {
                    parameters = MakeParameters(msg as ContextualMeshMessage);
                }
                catch {} //ignore
                var desc = "contextual mesh " + (parameters != null ? parameters.TilesetName : "(unknown)");
                if (verbose && parameters != null)
                {
                    int numWedges = (msg as ContextualMeshMessage).numWedges;
                    desc += string.Format(" for {0}; sols {1}; sitedrives {2} ({3} wedges)",
                                          parameters.RDRDir, MakeSolRanges(parameters.Sols),
                                          string.Join(",", parameters.SiteDrives),
                                          numWedges >= 0 ? numWedges.ToString() : "(unknown)");
                }
                return desc;
            }

            //get here if we're not running in master mode and msg is not a ContextualMeshMessage
            //should not happen, but if it does, our contract is not to throw
            return "unknown message type";
        }

        protected override QueueMessage DequeueOneMessage(MessageQueue queue, int overrideVisibilityTimeout = -1)
        {
            int ovt = overrideVisibilityTimeout;
            if (options.Master)
            {
                if (options.UseGenericMessageType)
                {
                    return queue.DequeueOne<GenericContextualMasterMessage>(overrideVisibilityTimeout: ovt);
                }
                else
                {
                    return queue.DequeueOne<SNSMessageWrapper>(overrideVisibilityTimeout: ovt);
                }
            }
            else
            {
                return queue.DequeueOne<ContextualMeshMessage>(overrideVisibilityTimeout: ovt);
            }
        }

        protected override QueueMessage ParseMessage(string json)
        {
            if (options.Master)
            {
                if (options.UseGenericMessageType)
                {
                    return JsonHelper.FromJson<GenericContextualMasterMessage>(json, autoTypes: false);
                }
                else
                {
                    return JsonHelper.FromJson<SNSMessageWrapper>(json, autoTypes: false);
                }
            }
            else
            {
                return JsonHelper.FromJson<ContextualMeshMessage>(json, autoTypes: false);
            }
        }

        protected override bool AcceptMessage(QueueMessage msg, out string reason)
        {
            reason = null;
            if (options.Master)
            {
                try
                {
                    string url = GetUrlFromMessage(msg); 
                    if (string.IsNullOrEmpty(url))
                    {
                        reason = "no URL in message";
                        return false;
                    }
                    if (StringHelper.GetUrlExtension(url).ToLower() != listExt)
                    {
                        reason = "unhandled file type: " + url;
                        return false;
                    }
                    if (ListFile.ParseSiteDriveListFilename(url, options.ListPrefix) == null)
                    {
                        reason = "unhandled list file name: " + url;
                        return false;
                    }
                    return true;
                }
                catch (Exception ex)
                {
                    reason = ex.Message;
                    return false;
                }
            }
            else if (msg is ContextualMeshMessage)
            {
                try
                {
                    return MakeParameters(msg as ContextualMeshMessage) != null; 
                }
                catch (Exception ex)
                {
                    reason = ex.Message;
                    return false;
                }
            }
            else
            {
                reason = "unknown message type";
                return false;
            }
        }

        protected override bool HandleMessage(QueueMessage msg)
        {
            if (options.Master)
            {
                string url = GetUrlFromMessage(msg); 
                if (!FileExists(url))
                {
                    pipeline.LogWarn("list file {0} not found", url);
                    return true; //drop message, maybe file was deleted or renamed
                }
                ProcessListFile(url); //throws exception on error
                return true; //successfully processed, remove message from queue
            }
            else if (msg is ContextualMeshMessage)
            {
                var parameters = MakeParameters(msg as ContextualMeshMessage);
                if (parameters == null)
                {
                    return true; //mission decided to ignore this message, remove it from the queue
                }
                BuildContextualTileset(parameters); //throws exception on error or if killed
                return true; //successfully processed, remove message from queue
            }
            else
            {
                pipeline.LogWarn("unknown message type, dropping message");
                return true;
            }
        }

        protected override bool ParseArguments()
        {
            if (options.Service && !options.Master)
            {
                options.UseGenericMessageType = true;
            }

            options.RecursiveSearch = !options.NoRecursiveSearch;

            if (!base.ParseArguments())
            {
                return false; //e.g. --help
            }

            options.LandformOwnedWorkerQueue |= options.LandformOwnedQueues;

            if (!(serviceMode || serviceUtilMode))
            {
                if (string.IsNullOrEmpty(options.RDRDir) ||
                    string.IsNullOrEmpty(options.Sols) || string.IsNullOrEmpty(options.SiteDrives))
                {
                    throw new Exception("--rdrdir, --sols, and --sitedrives required without --service or --master");
                }
            }
            else if (!string.IsNullOrEmpty(options.Sols) || !string.IsNullOrEmpty(options.SiteDrives))
            {
                throw new Exception("cannot combine --sols or --sitedrives with --service or --master");
            }

            if (options.Master)
            {
                listExt = options.ListFormat;
                if (string.IsNullOrEmpty(listExt))
                {
                    throw new Exception("empty list format");
                }
                listExt = "." + listExt.ToLower().TrimStart('.');

                if (!serviceUtilMode || options.DeleteQueues)
                {
                    if (string.IsNullOrEmpty(options.WorkerQueueName) && !options.DeleteQueues)
                    {
                        throw new Exception("--workerqueuename required with --master");
                    }
                    workerQueue = GetWorkerMessageQueue();
                }
            }

            return true;
        }

        protected override bool IsService()
        {
            return options.Service || (!serviceUtilMode && options.Master);
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

        protected override void DeleteQueues()
        {
            base.DeleteQueues();
            DeleteQueue(workerQueue, "worker");
        }

        protected override void RunService()
        {
            if (options.Master)
            {
                Task.Run(() => MasterLoop());
            }
            base.RunService();
        }

        private string GetUrlFromMessage(QueueMessage msg)
        {
            if (options.UseGenericMessageType)
            {
                return (msg as GenericContextualMasterMessage).listUrl;
            }
            else
            {
                if (!(msg is SNSMessageWrapper))
                {
                    throw new Exception("contextual master queue message does not have SNS wrapper");
                }
                return S3EventMessage.GetUrl(msg as SNSMessageWrapper, "ObjectCreated");
            }
        }
            
        private string MakeSolRanges(HashSet<int> sols, int primarySol = -1)
        {
            var ranges = new List<int[]>();
            var skipped = new List<int>();
            foreach (var sol in sols.OrderBy(sol => sol))
            {
                if (options.MaxSolRange >= 0 && primarySol >= 0 && Math.Abs(sol - primarySol) > options.MaxSolRange)
                {
                    skipped.Add(sol);
                    continue;
                }
                if (ranges.Count == 0 || ranges[ranges.Count - 1][1] != sol - 1)
                {
                    ranges.Add(new int[] { sol, sol });
                }
                else
                {
                    ranges[ranges.Count - 1][1] = sol;
                }
            }
            if (skipped.Count > 0)
            {
                pipeline.LogWarn("not including {0} sols out of range {1} from primary sol {2}: {3}",
                                 skipped.Count, options.MaxSolRange, primarySol, string.Join(", ", skipped));
            }
            return String.Join(",", ranges.Select(range => range[0] + (range[0] != range[1] ? ("-" + range[1]) : "")));
        }

        private ContextualMeshParameters MakeParameters(ContextualMeshMessage msg)
        {
            var ret = new ContextualMeshParameters();

            ret.RDRDir = !string.IsNullOrEmpty(msg.rdrDir) ? msg.rdrDir : options.RDRDir;
            
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
            string solStr = string.Format("{0:D4}", primarySol);
            string sdsStr = string.Join(",", siteDrives.ToArray());
            string project = string.Format("{0}_{1}", solStr, sdStr);
            string venue = string.Format("contextual_{0}_{1}", missionStr, project);
            string venueDir = storageDir + "/" + venue;
            string solDir = StringHelper.ReplaceFixedWidthIntWildcard(rdrDir, FetchData.SOL_WILDCARD, primarySol);
            string ingestDir = solDir;
            string fetchDir = !string.IsNullOrEmpty(options.FetchDir) ? options.FetchDir : storageDir + "/" + FETCH_DIR;
            string tilesetDir = GetTilesetDir(venue, sdStr, project);
            string destDir = GetDestDir(solDir);

            var orbitalCfg = OrbitalConfig.Instance;
            var orbitalDir = fetchDir + "/orbital/";
            Func<string, string, string> orbitalOpt = (opt, cfg) => !string.IsNullOrEmpty(opt) ? opt : cfg;
            string orbitalDEMUrl = orbitalOpt(options.OrbitalDEMURL, orbitalCfg.DEMURL);
            string orbitalDEMFile = orbitalOpt(options.OrbitalDEM, orbitalDir + orbitalCfg.DEMStoragePath);
            string orbitalImageUrl = orbitalOpt(options.OrbitalImageURL, orbitalCfg.ImageURL);
            string orbitalImageFile = orbitalOpt(options.OrbitalImage, orbitalDir + orbitalCfg.ImageStoragePath);
            string noOrbital = "";
            if (options.NoOrbital || (string.IsNullOrEmpty(orbitalDEMUrl) && string.IsNullOrEmpty(orbitalImageUrl)))
            {
                noOrbital = "--noorbital";
            }
            string noSurface = options.NoSurface ? "--nosurface" : "";

            pipeline.LogInfo("building contextual tileset {0} from {1} sitedrives in {2} sols",
                             project, siteDrives.Count, sols.Count);
            try
            {
                Cleanup(venueDir);

                Configure(venue);

                if (!options.NoFetch && rdrDir.StartsWith("s3://") && !(pipeline is CloudPipeline))
                {
                    ingestDir = fetchDir + "/rdrs";
                    Fetch(options.MaxFetch, MakeSolRanges(sols, primarySol), ingestDir, rdrDir,
                          "--onlyforsitedrives", sdsStr, "--summary");
                }

                if (!options.NoFetch && !options.NoOrbital)
                {
                    Action<string, string> fetchOrbitalAsset = (url, file) =>
                    {
                        if (!string.IsNullOrEmpty(url) && !string.IsNullOrEmpty(file))
                        {
                            string dir = Path.GetDirectoryName(file);
                            Fetch(options.MaxOrbital, url, dir, "--raw", "--nosubdirs");
                            string srcFile = StringHelper.GetLastUrlPathSegment(url);
                            string destFile = Path.GetFileName(file);
                            string fetchedFile = Path.Combine(dir, srcFile);
                            if (srcFile != destFile && File.Exists(fetchedFile))
                            {
                                PathHelper.MoveFileAtomic(fetchedFile, file); //overwrites existing
                            }
                        }
                    };
                    fetchOrbitalAsset(orbitalDEMUrl, orbitalDEMFile);
                    fetchOrbitalAsset(orbitalImageUrl, orbitalImageFile);
                }

                if (!options.NoIngest)
                {
                    if (sols.Count > 1 && ingestDir.StartsWith("s3://") && ingestDir == solDir && ingestDir != rdrDir)
                    {
                        throw new NotImplementedException("ingestion from multi-sol s3 wildcard not implemented");
                    }
                    RunCommand("ingest", project, "--mission", missionStr, "--onlyforsitedrives", sdsStr,
                               "--inputpath", ingestDir + "/" + (options.RecursiveSearch ? "**" : "*"),
                               noOrbital, noSurface, "--orbitaldem", orbitalDEMFile, "--orbitalimage", orbitalImageFile,
                               "--orbitalframe", sdStr);
                }

                if (!options.NoTileset)
                {
                    RunCommand("bev-align", options.AbortOnAlignmentError, project, "--fixsitedrives", sdStr);

                    RunCommand("heightmap-align", options.AbortOnAlignmentError, project, "--basesitedrive", sdStr);
                    
                    RunCommand("build-geometry", project, "--meshframe", sdStr);
                    
                    RunCommand("build-tiling-input", project, "--meshframe", sdStr);
                    
                    RunCommand("blend-images", project, "--meshframe", sdStr);
                    
                    BuildTileset(project, "--meshframe", sdStr);
                    
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

        /// <summary>
        /// Download and parse a list file.
        /// Returns null if the file doesn't exist or the filename is not recognized.
        /// Only keeps product IDs for observations used for meshing by the mission.
        /// </summary>
        private Stamped<ListFile> LoadListFile(string url)
        {
            if (!FileExists(url))
            {
                pipeline.LogWarn("list file {0} not found", url);
                return null;
            }
            var baseUrl = StringHelper.StripLastUrlPathSegment(StringHelper.StripLastUrlPathSegment(url));
            var sd = ListFile.ParseSiteDriveListFilename(url, options.ListPrefix);
            if (!sd.HasValue)
            {
                pipeline.LogWarn("unhandled list file name {0}", url);
                return null;
            }
            var preferredEye = mission.PreferEyeForGeometry();
            bool filter(RoverProductId id, int sol)
            {
                return mission.UseForMeshing(id) && RoverStereoPair.IsStereoEye(id.Camera, preferredEye);
            }
            var listFile = (new ListFile()).Load(GetFile(url), baseUrl, sd.Value, mission, accept: filter,
                                                 warn: msg => pipeline.LogWarn(msg),
                                                 error: msg => pipeline.LogError(msg));
            listFile.FilterProductIDs(mission.FilterProductIdGroups);
            return new Stamped<ListFile>(listFile);
        }

        /// <summary>
        /// Updates in-memory list file database for a list file that was added or updated on s3.
        /// If this is the first list file in its directory then all recognized list files in the directory are loaded.
        /// </summary>
        private void ProcessListFile(string url)
        {
            url = StringHelper.NormalizeUrl(url);

            pipeline.LogInfo("processing list file {0}", url);
                 
            var list = LoadListFile(url);

            if (list == null)
            {
                throw new Exception("failed to load list file " + url);
            }

            var listDir = StringHelper.StripLastUrlPathSegment(url);
            bool newDir = !listDirs.ContainsKey(listDir);
            var listsInDir = listDirs.GetOrAdd(listDir,
                                               _ => new Stamped<ConcurrentDictionary<string, Stamped<ListFile>>>());

            listsInDir.Value.AddOrUpdate(url, _ => list, (_, __) => list);

            if (newDir)
            {
                pipeline.LogInfo("loading siblings of list file {0}", url);
                int numOthers = 0;
                foreach (var other in SearchFiles(listDir.TrimEnd('/') + "/", //see PipelineCore.SearchFiles()
                                                  "*/" + options.ListPrefix + "*" + listExt,
                                                  recursive: false))
                {
                    string otherUrl = StringHelper.NormalizeUrl(other);
                    if (!url.Equals(otherUrl, StringComparison.OrdinalIgnoreCase))
                    {
                        var otherList = LoadListFile(otherUrl);
                        if (otherList != null)
                        {
                            listsInDir.Value.AddOrUpdate(otherUrl, _ => otherList, (_, __) => otherList);
                            numOthers++;
                        }
                        else
                        {
                            pipeline.LogWarn("failed to load sibling {0} of list file {1}", otherUrl, url);
                        }
                    }
                }
                pipeline.LogInfo("loaded {0} siblings of list file {1}", numOthers, url);
            }

            listsInDir.Touch();

            pipeline.DeleteDownloadCache();
        }

        /// <summary>
        /// Applys heruistics to possibly make a ContextualMesh for the sitedrive of primaryList.
        /// Returns null if it decided not to make one, or if there was a problem.
        /// If placesDB is null only the primary sitedrive is included (unless options.MaxSiteDriveDistance <= 0).
        /// Otherwise considers additional sitedrives from listFiles, which should all have same RDRDir as primaryList.
        /// </summary>
        private ContextualMeshMessage SiteDriveChanged(ListFile primaryList, List<ListFile> listFiles,
                                                       PlacesDB placesDB)
        {
            string rdrDir = primaryList.RDRDir;
            int primarySol = primaryList.MaxSol;
            SiteDrive primarySD = primaryList.SiteDrive;

            string name = string.Format("{0:D4}_{1}", primarySol, primarySD.ToString());

            int solRange = options.MaxSolRange >= 0 ? options.MaxSolRange : DEF_MAX_SOL_RANGE;
            int maxWedges = options.MaxContextualMeshWedges > 0 ? options.MaxContextualMeshWedges : int.MaxValue;
            int maxSDs = options.MaxSiteDrives > 0 ? options.MaxSiteDrives : int.MaxValue;
            double maxDistance = options.MaxSiteDriveDistance;

            int minSol = primarySol - solRange;
            int maxSol = primarySol + solRange;
            primaryList = solRange >= 0 ? primaryList.FilterToSolRange(minSol, maxSol) : primaryList;

            //primary site drive must have at least this many wedges
            int minWedges = Math.Max(options.MinSiteDriveWedges, options.MinPrimarySiteDriveWedges);
            if (primaryList.NumWedges < minWedges)
            {
                pipeline.LogInfo("not producing contextual mesh {0} in {1}, {2} < {3} wedges",
                                 name, rdrDir, primaryList.NumWedges, minWedges);
                return null;
            }

            //remaining sitedrives must have at least this many wedges
            minWedges = options.MinSiteDriveWedges;

            if (primaryList.NumWedges > maxWedges)
            {
                pipeline.LogInfo("not producing contextual mesh {0} in {1}, {2} > {3} wedges",
                                 name, rdrDir, primaryList.NumWedges, maxWedges);
                return null;
            }

            var keepers = new Dictionary<SiteDrive, ListFile>();
            keepers[primarySD] = primaryList;

            var distance = new Dictionary<SiteDrive, double>();

            if (placesDB != null || maxDistance <= 0)
            {
                foreach (var list in listFiles)
                {
                    if (!keepers.ContainsKey(list.SiteDrive))
                    {
                        var filtered = solRange >= 0 ? list.FilterToSolRange(minSol, maxSol) : list;
                        if (filtered != null && filtered.NumWedges >= minWedges)
                        {
                            if (maxDistance <= 0)
                            {
                                keepers[list.SiteDrive] = filtered;
                            }
                            else
                            {
                                try
                                {
                                    double dist = placesDB.GetOffset(primarySD, list.SiteDrive).Length();
                                    if (dist <= maxDistance)
                                    {
                                        keepers[list.SiteDrive] = filtered;
                                        distance[list.SiteDrive] = dist;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    pipeline.LogException(ex, string.Format("error getting distance from sitedrive " +
                                                                            "{0} to {1} from PlacesDB",
                                                                            primarySD, list.SiteDrive));
                                }
                            }
                        }
                    }
                }
            }

            int totalSDs = keepers.Count;
            int totalWedges = keepers.Values.Sum(list => list.NumWedges);
            if (maxSDs < int.MaxValue || maxWedges < int.MaxValue)
            {
                //default to deleting smaller sitedrives first
                var prioritized = keepers.Values
                    .Where(l => l.SiteDrive != primarySD)
                    .OrderBy(l => l.NumWedges)
                    .Select(l => l.SiteDrive)
                    .ToList();

                //if we have distances then delete further sitedrives first
                if (placesDB != null && maxDistance > 0)
                {
                    prioritized = prioritized.OrderByDescending(sd => distance[sd]).ToList();
                }

                var queue = new Queue<SiteDrive>(prioritized);
                while (queue.Count > 0 && (totalSDs > maxSDs || totalWedges > maxWedges))
                {
                    var dead = queue.Dequeue();
                    totalSDs--;
                    totalWedges -= keepers[dead].NumWedges;
                    keepers.Remove(dead);
                }
            }

            totalSDs = keepers.Count;
            totalWedges = keepers.Values.Sum(list => list.NumWedges);

            if (totalSDs == 0 || totalSDs > maxSDs) //should be redundant
            {
                pipeline.LogError("not producing contextual mesh {0} in {1}: {2} sitedrives (max {3})",
                                  name, rdrDir, totalSDs, maxSDs);
                return null;
            }

            if (totalWedges == 0 || totalWedges > maxWedges) //should be redundant
            {
                pipeline.LogError("not producing contextual mesh {0} in {1}: {2} wedges (max {3})",
                                  name, rdrDir, totalWedges, maxWedges);
                return null;
            }

            var sols = new HashSet<int>();
            sols.UnionWith(keepers.Values.SelectMany(l => l.Sols));

            return new ContextualMeshMessage()
            {
                rdrDir = rdrDir,
                primarySol = primarySol,
                primarySiteDrive = primarySD.ToString(),
                sols = MakeSolRanges(sols, primarySol),
                siteDrives = string.Join(",", keepers.Keys.OrderBy(sd => sd)),
                numWedges = totalWedges
            };
        }

        /// <summary>
        /// Combines a batch of new contextual mesh messages with existing ones in the worker queue.
        /// The messages must all have the same RDR dir.
        /// De-dupes, preferring newer-created messages to older.
        /// Returns messages sorted first by decreasing sol, then by decreasing number of wedges.
        /// </summary>
        private List<ContextualMeshMessage> CoalesceMessages(List<ContextualMeshMessage> newMsgsOldestToNewest)
        {
            if (newMsgsOldestToNewest.Count == 0)
            {
                return newMsgsOldestToNewest;
            }

            string rdrDir = newMsgsOldestToNewest[0].rdrDir;
            if (newMsgsOldestToNewest.Any(msg => msg.rdrDir != rdrDir))
            {
                throw new ArgumentException("all new messages must have same RDR dir");
            }
                 
            pipeline.LogInfo("coalescing {0} new messages with existing", newMsgsOldestToNewest.Count);

            //keep at most one message per (primarySol, primarySiteDrive) pair
            //ContextualMeshMessage defines its GetHashCode() and Equals() by (primarySol, primarySiteDrive)
            var keepers = new HashSet<ContextualMeshMessage>();

            void keepNewest(List<ContextualMeshMessage> msgs, string what)
            {
                for (int i = msgs.Count - 1; i >= 0; i--) //iterate newest -> oldest
                {
                    if (!keepers.Contains(msgs[i]))
                    {
                        keepers.Add(msgs[i]);
                    }
                    else
                    {
                        pipeline.LogInfo("{0} contextual mesh message superceded by a newer one, dropping: {1}",
                                         what, DescribeMessage(msgs[i], verbose: true));
                    }
                }
            }

            //it is possible, but unlikely, that there are dupes even in new messages
            keepNewest(newMsgsOldestToNewest, "new");

            //now reap all the existing messages in the worker queue for the same rdrDir
            //and keep any that aren't dupes of new messages
            //really there should be no dupes among the old messages
            //but just in case, keep them in order
            var oldMsgsOldestToNewest = new List<ContextualMeshMessage>();
            while (true)
            {
                var msg = workerQueue.DequeueOne<ContextualMeshMessage>() as ContextualMeshMessage;
                if (msg == null)
                {
                    break;
                }
                if (msg.rdrDir == rdrDir)
                {
                    workerQueue.DeleteMessage(msg);
                    oldMsgsOldestToNewest.Add(msg);
                }
            }
            pipeline.LogInfo("dequeued {0} existing messages", oldMsgsOldestToNewest.Count);

            keepNewest(oldMsgsOldestToNewest, "existing");

            pipeline.LogInfo("kept {0} coalesced messages from {1} old and {2} new",
                             keepers.Count, oldMsgsOldestToNewest.Count, newMsgsOldestToNewest.Count);

            //yes, OrderByDescending() is stable
            //https://stackoverflow.com/questions/1209935/orderby-and-orderbydescending-are-stable
            return keepers
                .OrderByDescending(msg => msg.numWedges) //lowest priority
                .OrderByDescending(msg => msg.primarySiteDrive) //medium priority
                .OrderByDescending(msg => msg.primarySol) //highest priority
                .ToList();
        }

        private MessageQueue GetWorkerMessageQueue()
        {
            return GetMessageQueue(options.WorkerQueueName, GetDefaultMessageTimeoutSec(),
                                   options.LandformOwnedWorkerQueue, "worker");
        }

        private DateTime ToLocalTime(long timestamp)
        {
            return UTCTime.MSSinceEpochToDate(timestamp).ToLocalTime();
        }

        private void MasterLoop()
        {
            double lastStartSec = -1;
            int targetPeriodSec = MASTER_LOOP_PERIOD_SEC;
            int debounceMS = 1000 * (options.MasterDebounceSec >= 0 ? options.MasterDebounceSec : DEF_DEBOUNCE_SEC);

            pipeline.LogInfo("worker queue: {0}", workerQueue.Name);
            pipeline.LogInfo("running master loop, period {0}s, debounce {1}s", targetPeriodSec, debounceMS / 1000);

            while (true)
            {
                if (lastStartSec >= 0)
                {
                    double actualPeriodSec = UTCTime.Now() - lastStartSec;
                    SleepSec(targetPeriodSec - actualPeriodSec); //negative ignored
                }
                lastStartSec = UTCTime.Now();

                try
                {
                    long now = (long)UTCTime.NowMS();
                    
                    //group list files by their RDR dir
                    //usually this results in only one group per list dir
                    //but in some odd cases, e.g. IDS pipeline version changed (cough, ROASTT20...), there are more
                    var stampedListsForRDRDir = new Dictionary<string, List<Stamped<ListFile>>>();
                    
                    foreach (var listDir in listDirs.Keys)
                    {
                        foreach (var stampedList in listDirs[listDir].Value.Values)
                        {
                            var rdrDir = stampedList.Value.RDRDir;
                            if (!stampedListsForRDRDir.ContainsKey(rdrDir))
                            {
                                stampedListsForRDRDir[rdrDir] = new List<Stamped<ListFile>>();
                            }
                            stampedListsForRDRDir[rdrDir].Add(stampedList);
                        }
                    }

                    //try to connect to PlacesDB just for this pass
                    //we do that for a couple of reasons rather than having a single long-lived PlacesDB connection
                    //for one thing our PlacesDB interface caches results
                    //so if the underlying answers were to be updated refine, it could be stale over time
                    //also, particularly in certain dev scenarios, PlacesDB availability may be iffy
                    //better to try on each pass rather than once ever
                    PlacesDB placesDB = null;
                    var placesCfg = PlacesConfig.Instance;
                    bool usePlaces = !string.IsNullOrEmpty(placesCfg.Url) && !string.IsNullOrEmpty(placesCfg.View);

                    foreach (var rdrDir in stampedListsForRDRDir.Keys)
                    {
                        var msgs = new List<Stamped<ContextualMeshMessage>>();

                        var stampedLists = stampedListsForRDRDir[rdrDir];
                        var listFiles = stampedLists.Select(sl => sl.Value).ToList();

                        bool firstPass = !lastMasterPass.ContainsKey(rdrDir);

                        var changedLists = firstPass ? stampedLists :
                            stampedLists.Where(sl => sl.Timestamp > lastMasterPass[rdrDir]).ToList();

                        if (changedLists.Count > 0)
                        {
                            long lastChange = changedLists.Max(sl => sl.Timestamp);
                            long firstChange = changedLists.Min(sl => sl.Timestamp);
                            
                            long debounceThreshold = debounceMS <= 0 ? now : now - debounceMS;
                            
                            if (debounceThreshold == now || lastChange < debounceThreshold)
                            {
                                //at least one list file for rdrDir has been updated since we last made a pass over them
                                //but the most recently updated one changed at least debounceMS ago
                                
                                pipeline.LogInfo("making pass on RDR dir {0} at {1} ({2}s debounce threshold {3})",
                                                 rdrDir, ToLocalTime(now), debounceMS / 1000,
                                                 ToLocalTime(now - debounceMS));
                                
                                pipeline.LogInfo("{0} list files, {1} changed since last pass at {2}, " +
                                                 "first changed time {3}, last changed time {4}",
                                                 listFiles.Count, changedLists.Count, firstPass ? "(never)" :
                                                 ToLocalTime(lastMasterPass[rdrDir]).ToString(),
                                                 ToLocalTime(firstChange), ToLocalTime(lastChange));
                                
                                pipeline.LogInfo("{0} sitedrives, min sol {1}, max sol {2}",
                                                 listFiles.Select(l => l.SiteDrive).Distinct().Count(),
                                                 listFiles.Min(l => l.MinSol), listFiles.Max(l => l.MaxSol));
                                
                                if (placesDB == null && usePlaces)
                                {
                                    try
                                    {
                                        placesDB = new PlacesDB(pipeline);
                                    }
                                    catch (Exception ex)
                                    {
                                        pipeline.LogError("error initializing PlacesDB: {0}", ex.Message);
                                        usePlaces = false;
                                    }
                                }
                                pipeline.LogInfo("{0}using PlacesDB {1}",
                                                 usePlaces ? "" : "not ", usePlaces ? placesCfg.Url : "");
                                
                                foreach (var stampedList in changedLists)
                                {
                                    try
                                    {
                                        var msg = SiteDriveChanged(stampedList.Value, listFiles, placesDB);
                                        if (msg != null)
                                        {
                                            msgs.Add(new Stamped<ContextualMeshMessage>(msg, stampedList.Timestamp));
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        pipeline.LogException(ex, "error processing sitedrive " +
                                                              stampedList.Value.SiteDrive);
                                    }
                                }
                                
                                lastMasterPass[rdrDir] = now;
                            }
                        }
                        
                        //order messages according to when the corersponding list file changed (oldest to newest)
                        var rawMsgs = msgs.OrderBy(sm => sm.Timestamp).Select(sm => sm.Value).ToList();
                        
                        if (msgs.Count > 0)
                        {
                            try
                            {
                                rawMsgs = CoalesceMessages(rawMsgs);
                            }
                            catch (Exception ex)
                            {
                                pipeline.LogException(ex, "error coalescing messages, proceeding with un-coaleseced");
                            }
                            
                            //TODO right about here we should try to determine if any workers
                            //are processing contextual meshes for which there are new messages
                            //and if so, ask them to abort
                            //https://github.jpl.nasa.gov/OnSight/Landform/issues/1026
                            
                            pipeline.LogInfo("enqueueing {0} contextual mesh messages to {1} for {2}",
                                             rawMsgs.Count, workerQueue.Name, rdrDir);
                            
                            foreach (var msg in rawMsgs) //in order starting with highest sol, largest number of wedges
                            {
                                try
                                {
                                    pipeline.LogInfo("enqueueing contextual mesh message to {0}: {1}",
                                                     workerQueue.Name, DescribeMessage(msg, verbose: true));
                                    workerQueue.Enqueue(msg);
                                }
                                catch (Exception ex)
                                {
                                    pipeline.LogException(ex, "adding message to worker queue");
                                }
                            }
                        }
                    }
                }
                catch (Exception masterException)
                {
                    pipeline.LogException(masterException, string.Format("error in master loop, retrying in {0}",
                                                                         Fmt.HMS(SERVICE_LOOP_RETRY_SEC * 1000)));
                    SleepSec(SERVICE_LOOP_RETRY_SEC);
                }
            }
        }
    }
}
