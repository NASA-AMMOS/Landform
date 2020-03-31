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
/// Also see Scripts/processContextual.sh, which has overlapping functionality for the batch-mode case.
/// (processContextual.sh does not implement the service case.)  processContextual.sh is intended for use by developers
/// only, and has additional options for development and debugging workflows.  process-contextual (ProcessContextual.cs)
/// can be used by developers but is mainly intended for deployment and production use.
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
///    --mastertoworkerqueuename=landform-contextual
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

        [Option(Required = false, Default = null, HelpText = "Max fetched RDR bytes on disk, not including orbital, integer with optional case-insensitive suffix K,M,G, unlimited if empty or non-positive")]
        public string MaxFetch { get; set; }

        [Option(Required = false, Default = null, HelpText = "Max fetched orbital bytes on disk, integer with optional case-insensitive suffix K,M,G, unlimited if empty or non-positive")]
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

        [Option(HelpText = "Disable orbital", Default = false)]
        public bool NoOrbital { get; set; }

        [Option(HelpText = "Abort contextual mesh workflow on unexpected error in an alignment stage", Default = false)]
        public bool AbortOnAlignmentError { get; set; }

        [Option(HelpText = "Run as contextual mesh master service", Default = false)]
        public bool Master { get; set; }

        [Option(Required = false, Default = "lis", HelpText = "Master service list filename extension")]
        public string ListFormat { get; set; }

        [Option(Required = false, Default = "xyz_", HelpText = "Master service list filename prefix")]
        public string ListPrefix { get; set; }

        [Option(Required = false, Default = null, HelpText = "Master to worker message queue name, reuquired with --master")]
        public string MasterToWorkerQueueName { get; set; }

        [Option(Required = false, Default = false, HelpText = "Master to worker message queue is Landform owned")]
        public bool LandformOwnedMasterToWorkerQueue { get; set; }

        [Option(Required = false, Default = 4, HelpText = "Minimum number of wedges for a base site drive in a contextual mesh")]
        public int MinBaseSiteDriveWedges { get; set; }

        [Option(Required = false, Default = 1, HelpText = "Minimum number of wedges for a site drive to be included in a contextual mesh")]
        public int MinSiteDriveWedges { get; set; }

        [Option(Required = false, Default = 100, HelpText = "Maximum number of wedges for which to build a contextual mesh")]
        public int MaxContextualMeshWedges { get; set; }

        [Option(Required = false, Default = 10, HelpText = "Max number of site drives to include in contextual mesh")]
        public int MaxSiteDrives{ get; set; }

        [Option(Required = false, Default = 32, HelpText = "Max distance in meters from origin of base site drive to origin of a site drive to include in contextual mesh")]
        public double MaxSiteDriveDistance { get; set; }

        [Option(Required = false, Default = 30, HelpText = "Max difference between sols in base site drive and site drive to include in contextual mesh")]
        public int MaxSolDistance { get; set; }
    }

    public class ProcessContextual : LandformService
    {
        public const string FETCH_DIR = "fetched";

        public const int DEF_MAX_HANDLER_SEC = 2 * 60 * 60; //2 hours
        public const int DEF_MAX_MESSAGE_AGE_SEC = 6 * 60 * 60; //6 hours

        public const int DEF_MASTER_MAX_HANDLER_SEC = 10 * 60; //10 minutes
        public const int DEF_MASTER_MAX_MESSAGE_AGE_SEC = 1 * 60 * 60; //1 hour

        protected ProcessContextualOptions options;

        private string listExt;

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

        private class GenericContextualMasterMessage : QueueMessage
        {
#pragma warning disable 0649
            public string listUrl;
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
            if (options.Master)
            {
                string url = null;
                try
                {
                    url = GetUrlFromMessage(msg);
                }
                catch {} //ignore
                return "xyz list file " + (url ?? "(unknown)");
            }
            else
            {
                ContextualMeshParameters parameters = null;
                try
                {
                    parameters = MakeParameters(msg);
                }
                catch {} //ignore
                var desc = "contextual mesh " + (parameters != null ? parameters.TilesetName : "(unknown)");
                if (verbose && parameters != null)
                {
                    desc += string.Format(" for {0}; sols {1}; sitedrives {2}",
                                          parameters.RDRDir, parameters.Sols, parameters.SiteDrives);
                }
                return desc;
            }
        }

        protected override QueueMessage DequeueOneMessage(MessageQueue queue)
        {
            if (options.Master)
            {
                if (options.UseGenericMessageType)
                {
                    return messageQueue.DequeueOne<GenericContextualMasterMessage>();
                }
                else
                {
                    return queue.DequeueOne<SNSMessageWrapper>();
                }
            }
            else
            {
                //non-master mode implies generic message type
                return messageQueue.DequeueOne<GenericContextualMeshMessage>();
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
                //non-master mode implies generic message type
                return JsonHelper.FromJson<GenericContextualMeshMessage>(json, autoTypes: false);
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
                        reason = "unhandled listfile name: " + url;
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
            else
            {
                try
                {
                    return MakeParameters(msg) != null; 
                }
                catch (Exception ex)
                {
                    reason = ex.Message;
                    return false;
                }
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
                ProcesssListFile(url); //throws exception on error
            }
            else
            {
                var parameters = MakeParameters(msg);
                if (parameters == null)
                {
                    return true; //mission decided to ignore this message, remove it from the queue
                }
                BuildContextualTileset(parameters); //throws exception on error or if killed
            }
            return true; //successfully processed, remove message from queue
        }

        protected override bool ParseArguments()
        {
            if (options.Service && !options.Master)
            {
                options.UseGenericMessageType = true;
            }
            options.Service |= options.Master;

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

        private ContextualMeshParameters MakeParameters(QueueMessage msg)
        {
            //non-master mode implies generic message type
            return MakeParameters((GenericContextualMeshMessage)msg, options.RDRDir);
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

            string demURL = !string.IsNullOrEmpty(options.OrbitalDEMURL) ? options.OrbitalDEMURL
                : OrbitalConfig.Instance.OrbitalDEMURL;
            string demFile = !string.IsNullOrEmpty(options.OrbitalDEM) ? options.OrbitalDEM
                : fetchDir + "/orbital/" + OrbitalConfig.Instance.OrbitalDEMStoragePath;
            string noOrbital = (options.NoOrbital || string.IsNullOrEmpty(demURL)) ? "--noorbital" : "";

            var allowedFetchFlags = new HashSet<string>() { "--quiet", "--verbose", "--debug", "--nosave" };

            pipeline.LogInfo("building contextual tileset {0} from {1} sitedrives in {2} sols",
                             project, siteDrives.Count, sols.Count);
            try
            {
                Cleanup(venueDir);

                Configure(venue);

                if (!options.NoFetch && rdrDir.StartsWith("s3://") && !(pipeline is CloudPipeline))
                {
                    ingestDir = fetchDir + "/rdrs";
                    RunCommand("fetch", allowedFetchFlags, GetSolRanges(sols), ingestDir, rdrDir,
                               "--onlyforsitedrives", sdsStr, "--summary",
                               "--maxdownload", options.MaxFetch, "--accountexisting", "--deletelru",
                               "--mission", missionStr, "--awsprofile", awsProfile, "--awsregion", awsRegion);
                }

                if (!options.NoFetch && !options.NoOrbital && !string.IsNullOrEmpty(demURL))
                {
                    string dir = Path.GetDirectoryName(demFile);
                    RunCommand("fetch", allowedFetchFlags, demURL, dir, "--raw", "--nosubdirs",
                               "--maxdownload", options.MaxOrbital, "--accountexisting", "--deletelru",
                               "--mission", missionStr, "--awsprofile", awsProfile, "--awsregion", awsRegion);

                    string srcFile = StringHelper.GetLastUrlPathSegment(demURL);
                    string destFile = Path.GetFileName(demFile);
                    string fetchedFile = Path.Combine(dir, srcFile);

                    if (srcFile != destFile && File.Exists(fetchedFile))
                    {
                        PathHelper.MoveFileAtomic(fetchedFile, demFile); //overwrites existing
                    }
                }

                if (!options.NoIngest)
                {
                    if (sols.Count > 1 && ingestDir.StartsWith("s3://") && ingestDir == solDir && ingestDir != rdrDir)
                    {
                        throw new NotImplementedException("ingestion from multi-sol s3 wildcard not implemented");
                    }
                    RunCommand("ingest", project, "--mission", missionStr, "--onlyforsitedrives", sdsStr,
                               "--inputpath", ingestDir + "/" + (options.RecursiveSearch ? "**" : "*"));
                }

                if (!options.NoTileset)
                {
                    RunCommand("bev-align", options.AbortOnAlignmentError, project, "--fixsitedrives", sdStr);

                    RunCommand("heightmap-align", options.AbortOnAlignmentError, project, "--basesitedrive", sdStr,
                               noOrbital, "--orbitaldem", demFile);
                    
                    RunCommand("build-geometry", project, "--meshframe", sdStr,
                               noOrbital, "--orbitaldem", demFile);
                    
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

        private void ProcesssListFile(string url)
        {
            //TODO
        }
    }
}
