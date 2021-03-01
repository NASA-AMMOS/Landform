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
using System.Text;
using System.Text.RegularExpressions;
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
/// 8. build-sky-sphere
/// 9. update-scene-manifest (manifest just for the contextual mesh tileset with relative URLs)
/// 10. update-scene-manifest (optional combined manifest for the scene with abolute URLs)
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
/// Also see ProcessTactical.cs and process-tactical.sh which automate the tactical mesh tileset workflow.
///
/// A contextual mesh is generated for a specific primary sol and primary sitedrive.  It combines data from a set of
/// sols and sitedrives (which must contain the primary sol/sitedrive), as well as orbital assets if available.  (Note
/// PlacesDB is also required to include orbital in contextual meshes.)
///
/// When run as a command line tool the sols and sitedrives to use are given by the --sols and --sitedrives options,
/// where the first listed sol and sitedrive are primary.  When run as a service they are included in the SQS messages.
///
/// RDRs are fetched (or in the case of local files, read from disk) from a specified directory, recursively by default.
/// If operating on multiple sols the RDR directory can contain a ##### wildcard which will be replaced with each sol
/// number.
///
/// Example RDR directory specifiers:
/// * "s3://BUCKET/ods/VER/sol/#####/ids/rdr"
/// * "s3://BUCKET/ods/VER/YYYY/###/ids/rdr"
/// * "s3://BUCKET/foo/bar"
/// * "c:/foo/bar"
/// * "./foo/bar"
///
/// The output tileset is named TTTT_SSSDDDD where TTTT is the primary sol and SSSDDDD is the primary sitedrive.  It is
/// written to rdrDir/tileset/TTTT_SSSDDDD (*), unless --outputfolder is specified, in which case it is written to a
/// subdirectory TTTT_SSSDDDD there.
///
/// (*) Actually if rdrDir contains a prefix ending /rdr then the output directory is that prefix but with rdr replaced
/// with rdr/tileset/TTTT_SSSDDDD.
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
/// Unless --nosky is specified all of the above also holds for a sky tileset named TTTT_SSSDDDD_sky.
/// 
/// A combined scene manifest with absolute URLs can also be optionally created or updated as a sibling of the output
/// tileset directory.  In that case the update-scene-manifest tool will also include any sibling tactical mesh tilesets
/// in the manifest.
///
/// This service can also run in master service mode by specifying --master.  In that mode the service listens for
/// messages indicating XYZ list files and/or XYZ wedge files have been created or updated.  There is generally one list
/// file per sitedrive, listing the XYZ wedge RDRs for it.  Once changes have stopped for at least a minimum debounce
/// period, the master optionally scans for other list and/or wedge files and uses them to develop lists of changed
/// sitedrives, available sitedrives, and the number of wedges in each sitedrive.  Using PlacesDB to determine distances
/// between sitedrives, it may then produce one or more contextual mesh messages based on various parameters which limit
/// the minimum size of a sitedrive for which a contextual mesh is built, the maximum range of sols for which to include
/// adjacent sitedrives, the maximum distance for adjacent sitedrives, and the maximum number of wedges to include in a
/// contextual mesh.  If PlacesDB is not available then contextual meshes will only be built for single sitedrives.
///
/// * Run as service:
///
/// Landform.exe process-contextual --service --mission=M2020 \
///    --queuename=landform-contextual --failqueuename=landform-contextual-fail
///
/// * Run as master service:
///
/// Landform.exe process-contextual --master --mission=M2020 \
///    --queuename=landform-contextual-master --failqueuename=landform-contextual-master-fail \
///    --workerqueuename=landform-contextual
///
/// * Windjana in batch mode using already downloaded RDRs:
///
/// Landform.exe process-contextual --mission=M2020 --rdrdir=out/windjana/rdrs --sols=0609-0630
///   --sitedrives=0311472,0311256,0311444,0311330 --nocombinedmanifest
///
/// * MSL orbital only (assuming out/windjana/orbital-only does not contain RDRs; add --nosurface --notileset
/// --nocombinedmanifest to just ingest and spew oribtal metadata):
///
/// Landform.exe process-contextual --mission=MSL --rdrdir=out/windjana/orbital-only --sols=0609 --sitedrives=0311472
///
/// * M2020 orbital only without PlacesDB:
///
/// export LANDFORM_ORBITAL_ALLOW_EXPECTED_LANDING_LON_LAT=true
/// Landform.exe process-contextual --mission=M2020 --rdrdir=out/M2020-landing-orbital --sols=0001 --sitedrives=0010000
///
/// * process contextual mesh from S3 to local folder:
///
/// Landform.exe process-contextual --mission=M2020 --rdrdir=s3://m20-ids-g-data-g66bt/ods/dev/sol/#####/ids/rdr
/// --sols=0281 --sitedrives=0160354 --outputfolder=out/g66bt-281
///
/// add --dryrun for dry run
/// add --notileset --nocombinedmanifest --nocleanup to just ingest and leave database
/// </summary>
namespace OPS.Landform
{
    [Verb("process-contextual", HelpText = "process contextual meshes")]
    public class ProcessContextualOptions : LandformServiceOptions
    {
        [Value(0, Required = false, Default = null, HelpText = "option disabled for this command")]
        public override string ProjectName { get; set; }

        [Option(Default = null, HelpText = "Output directory or S3 folder, if unset use same folder as input")]
        public override string OutputFolder { get; set; }

        [Option(Default = null, HelpText = "Input directory or S3 folder with sol replaced with #####, optional with --service")]
        public string RDRDir { get; set; }

        [Option(Default = null, HelpText = "Sol(s) and range(s) with primary one first, e.g. 8,6-10, mutually exclusive with --service")]
        public string Sols { get; set; }

        [Option(Default = null, HelpText = "Sitedrives with primary one first, mutually exclusive with --service")]
        public string SiteDrives { get; set; }

        [Option(Default = false, HelpText = "Don't fetch")]
        public bool NoFetch { get; set; }

        [Option(Default = null, HelpText = "Persistent download dir, defaults to \"fetched\" subdir of local Landform storage dir")]
        public string FetchDir { get; set; }

        [Option(Default = null, HelpText = "Max fetched RDR bytes on disk, not including orbital, integer with optional case-insensitive suffix K,M,G, no limit if omitted or non-positive")]
        public string MaxFetch { get; set; }

        [Option(Default = null, HelpText = "Max fetched orbital bytes on disk, integer with optional case-insensitive suffix K,M,G, no limit if empty or non-positive")]
        public string MaxOrbital { get; set; }

        [Option(Default = false, HelpText = "Don't ingest")]
        public bool NoIngest { get; set; }

        [Option(Default = false, HelpText = "Don't generate tileset")]
        public bool NoTileset { get; set; }

        [Option(Default = false, HelpText = "Don't generate sky sphere tileset")]
        public bool NoSky { get; set; }

        [Option(HelpText = "Sky mode (Box, Sphere, TopoSphere, Auto)", Default = SkyMode.Auto)]
        public SkyMode SkyMode { get; set; }

        [Option(HelpText = "Sky sphere radius (meters), or auto", Default = "auto")]
        public string SkySphereRadius { get; set; }

        [Option(HelpText = "Minimum sky backproject radius (meters), or auto", Default = "auto")]
        public string SkyMinBackprojectRadius { get; set; }

        [Option(Default = false, HelpText = "Don't write/update combined scene manifest on s3")]
        public bool NoCombinedManifest { get; set; }

        [Option(Default = false, HelpText = "Don't recursively search for RDRs")]
        public bool NoRecursiveSearch { get; set; }

        [Option(Default = true, HelpText = "option disabled for this command")]
        public override bool RecursiveSearch { get; set; }

        [Option(Default = false, HelpText = "option disabled for this command")]
        public override bool CaseSensitiveSearch { get; set; }

        [Option(Default = null, HelpText = "option disabled for this command")]
        public override string MeshFormat { get; set; }

        [Option(Default = null, HelpText = "option disabled for this command")]
        public override string ImageFormat { get; set; }

        [Option(Default = null, HelpText = "Override default orbital DEM file path")]
        public string OrbitalDEM { get; set; }

        [Option(Default = null, HelpText = "Override default orbital DEM URL")]
        public string OrbitalDEMURL { get; set; }

        [Option(Default = null, HelpText = "Override default orbital image file path")]
        public string OrbitalImage { get; set; }

        [Option(Default = null, HelpText = "Override default orbital image URL")]
        public string OrbitalImageURL { get; set; }

        [Option(Default = false, HelpText = "Abort contextual mesh workflow on unexpected error in an alignment stage")]
        public bool AbortOnAlignmentError { get; set; }

        [Option(Default = BuildGeometry.DEF_SURFACE_EXTENT, HelpText = "Surface geometry extent in meters")]
        public double SurfaceExtent { get; set; }

        [Option(Default = BuildGeometry.DEF_EXTENT, HelpText = "Combined surface and orbital geometry extent in meters")]
        public double Extent { get; set; }

        [Option(Default = false, HelpText = "Run as contextual mesh master service")]
        public bool Master { get; set; }

        [Option(Default = null, HelpText = "Master service list filename pattern, case insensitive, e.g. xyz_*.lis, null, empty, or \"none\" to reject list files")]
        public string ListPattern { get; set; }

        [Option(Default = "*XYZ*.IMG", HelpText = "Master service wedge filename pattern, case insensitive, null, empty,or \"none\" to reject wedge files")]
        public string WedgePattern { get; set; }

        [Option(Default = "*RAS*.IMG", HelpText = "Master service texture filename pattern, case insensitive, null, empty,or \"none\" to reject texture files")]
        public string TexturePattern { get; set; }

        [Option(Default = false, HelpText = "If using list files (--listpattern is specified) then don't search for sibling list files to detect available sitedrives")]
        public bool NoSearchForAdditionalLists { get; set; }

        [Option(Default = false, HelpText = "If using wedge files (--wedgepattern is specified) then don't search for other wedges in the same RDR directory to detect available sitedrives")]
        public bool NoSearchForAdditionalWedges { get; set; }

        [Option(Default = null, HelpText = "Worker message queue name, required with --master")]
        public string WorkerQueueName { get; set; }

        [Option(Default = ProcessContextual.DEF_DEBOUNCE_SEC, HelpText = "Master waits at least this long after any list or wedge file changed for a given RDR directory before firing a new contextual mesh message, default if negative")]
        public int MasterDebounceSec { get; set; }

        [Option(Default = false, HelpText = "Worker message queue is Landform owned")]
        public bool LandformOwnedWorkerQueue { get; set; }

        [Option(Default = 4, HelpText = "Minimum number of wedges for primary site drive in a contextual mesh, non-positive for no limit")]
        public int MinPrimarySiteDriveWedges { get; set; }

        [Option(Default = 500, HelpText = "Maximum number of wedges for which to build a contextual mesh (does not apply to primary sitedrive; culls additional sitedrives in order of increasing distance, then size), non-positive for no limit")]
        public int MaxContextualMeshWedges { get; set; }

        [Option(Default = 10, HelpText = "Max number of site drives to include in contextual mesh, non-positive for no limit")]
        public int MaxSiteDrives{ get; set; }

        //https://github.jpl.nasa.gov/OnSight/Landform/issues/1105
        [Option(Default = BuildGeometry.DEF_SURFACE_EXTENT, HelpText = "Max distance in meters from origin of a site drive to origin of primary site drive to include in contextual mesh, non-positive for no limit")]
        public double MaxSiteDriveDistance { get; set; }

        [Option(Default = ProcessContextual.DEF_MAX_SOL_RANGE, HelpText = "Max difference between sol and primary sol to include in contextual mesh, negative to use default")]
        public int MaxSolRange { get; set; }

        [Option(HelpText = "Allow rover observations for which no suitable rover mask is available or could be generated", Default = false)]
        public virtual bool AllowUnmaskedRoverObservations { get; set; }
    }

    public class ProcessContextual : LandformService
    {
        public const string FETCH_DIR = "fetched";

        new public const int DEF_MAX_HANDLER_SEC = 4 * 60 * 60; //4 hours
        new public const int DEF_MAX_MESSAGE_AGE_SEC = 6 * 60 * 60; //6 hours

        public const int DEF_MASTER_MAX_HANDLER_SEC = 10 * 60; //10 minutes
        public const int DEF_MASTER_MAX_MESSAGE_AGE_SEC = 1 * 60 * 60; //1 hour

        public const int MASTER_LOOP_PERIOD_SEC = 10;

        //currently up to ~5min gaps in XYZ IMG within one pass
        //but PlacesDB orbital solutions may not stabilize for up to 25 min after generation
        //of navcam orthomosaics
        public const int DEF_DEBOUNCE_SEC = 30 * 60; //30 minutes

        public const int DEF_MAX_SOL_RANGE = 200;

        protected ProcessContextualOptions options;

        private Regex listRegex, wedgeRegex, textureRegex;

        private int debounceMS;
        private int solRange, maxWedges, maxSDs;

        private MessageQueue workerQueue;

        //message sent from master to worker
        //defines the job of building one contextual mesh
        //equality is based only on rdrDir, primarySol, primarySiteDrive
        private class ContextualMeshMessage : QueueMessage
        {
            //designed for serialization to JSON so using camelCase not StudlyCaps
#pragma warning disable 0649
            public string rdrDir; //e.g. s3://BUCKET/ods/VER/sol/#####/ids/rdr; if null or empty then use options.RDRDir
            public int primarySol;
            public string sols; //e.g. 2,3,4-9,14; if null or empty then use primarySol
            public string primarySiteDrive;
            public string siteDrives; //e.g. 0230001,0230002,0240001; if null or empty then use primarySiteDrive
            public int numWedges = -1; //used only for information and sorting, negative if unknown
            public long timestamp; //UTC milliseconds since epoch when message was created
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
        }

        private string Dump(ContextualMeshParameters parameters, bool verbose = false, ContextualMeshMessage cmm = null)
        {
            var ret = string.Format("contextual mesh {0}_{1}",
                                    SolToString(parameters.PrimarySol), parameters.PrimarySiteDrive);
            if (verbose)
            {
                ret += string.Format(" for {0}; sols {1}; sitedrives {2}",
                                     parameters.RDRDir, MakeSolRanges(parameters.Sols),
                                     string.Join(",", parameters.SiteDrives));
                if (cmm != null)
                {
                    ret += string.Format("; {0} wedges; timestamp {1} UTC",
                                         cmm.numWedges >= 0 ? cmm.numWedges.ToString() : "??",
                                         cmm.timestamp > 0 ? UTCTime.MSSinceEpochToDate(cmm.timestamp).ToString()
                                         : "??");
                }
            }
            return ret;
        }

        //RDR directory -> sitedrive -> list or wedge URL -> last changed UTC milliseconds
        //list and wedge URLs for which we recently recived ObjectCreated (i.e. changed) messages
        //when MasterLoop() sees that none have changed for a given RDR directory in at least options.MasterDebounceSec
        //then that directory will be processed and its changed URLs cleared
        //synchronization is by locking this object itself
        private Dictionary<string, Dictionary<SiteDrive, Dictionary<string, long>>> changedURLs =
            new Dictionary<string, Dictionary<SiteDrive, Dictionary<string, long>>>();
            
        public ProcessContextual(ProcessContextualOptions options) : base(options)
        {
            this.options = options;
            defMaxHandlerSec = options.Master ? DEF_MASTER_MAX_HANDLER_SEC : DEF_MAX_HANDLER_SEC;
            defMaxMessageAgeSec = options.Master ? DEF_MASTER_MAX_MESSAGE_AGE_SEC : DEF_MAX_MESSAGE_AGE_SEC;
        }

        protected override void RunBatch()
        {
            RunPhase("build contextual tileset ",
                     () => BuildContextualTileset(MakeParameters(options.RDRDir, options.Sols, options.SiteDrives)));
        }

        protected override string DescribeMessage(QueueMessage msg, bool verbose = false)
        {
            if (msg is ContextualMeshMessage)
            {
                try
                {
                    var cmm = (msg as ContextualMeshMessage);
                    var parameters = MakeParameters(cmm);
                    if (parameters == null)
                    {
                        return "(invalid contextual mesh message)";
                    }
                    return Dump(parameters, verbose, cmm);
                }
                catch (Exception ex) //this entire method is never supposed to throw
                {
                    string m = "error parsing contextual mesh message";
                    pipeline.LogException(ex, m);
                    return $"({m}: {ex.Message})";
                }
            }
            else
            {
                return base.DescribeMessage(msg, verbose);
            }
        }

        protected override QueueMessage DequeueOneMessage(MessageQueue queue, int overrideVisibilityTimeout = -1)
        {
            int ovt = overrideVisibilityTimeout;
            return options.Master ? base.DequeueOneMessage(queue, ovt)
                : queue.DequeueOne<ContextualMeshMessage>(overrideVisibilityTimeout: ovt);
        }

        protected override QueueMessage ParseMessage(string json)
        {
            return options.Master ? base.ParseMessage(json)
                : JsonHelper.FromJson<ContextualMeshMessage>(json, autoTypes: false);
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
                    string file = StringHelper.GetLastUrlPathSegment(url);
                    bool isList = listRegex != null && listRegex.IsMatch(file);
                    bool isWedge = wedgeRegex != null && wedgeRegex.IsMatch(file);
                    bool isTexture = textureRegex != null && textureRegex.IsMatch(file);
                    if (!isList && !isWedge && !isTexture)
                    {
                        reason = "unhandled file type: " + url;
                        return false;
                    }
                    if (!AcceptBucketPath(url, allowInternal: isList))
                    {
                        reason = "rejected bucket path: " + url;
                        return false;
                    }
                    if (isWedge)
                    {
                        var idStr = StringHelper.GetLastUrlPathSegment(url, stripExtension: true);
                        var id = RoverProductId.Parse(idStr, mission, throwOnFail: false);
                        reason = FilterWedge(id);
                        if (reason != null)
                        {
                            reason += ": " + url;
                            return false;
                        }
                    }
                    if (isTexture)
                    {
                        var idStr = StringHelper.GetLastUrlPathSegment(url, stripExtension: true);
                        var id = RoverProductId.Parse(idStr, mission, throwOnFail: false);
                        reason = FilterTexture(id);
                        if (reason != null)
                        {
                            reason += ": " + url;
                            return false;
                        }
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
                reason = "unknown message type " + msg.GetType().Name;
                return false;
            }
        }

        protected override bool HandleMessage(QueueMessage msg)
        {
            if (options.Master)
            {
                string url = StringHelper.NormalizeUrl(GetUrlFromMessage(msg)); 
                if (!FileExists(url))
                {
                    pipeline.LogWarn("file {0} not found", url);
                    return true; //drop message, maybe file was deleted or renamed
                }
                var rdrDir = SiteDriveList.GetRDRDir(url);
                if (rdrDir == null && listRegex != null && listRegex.IsMatch(StringHelper.GetLastUrlPathSegment(url)))
                {
                    //this will happen e.g. for s3://BUCKET/ids-pipeline/xyz_SSSDDDD.lis
                    //this is maybe a bit wasteful but the download should be cached
                    //and the cost of parsing the list file multiple times shouldn't be a big deal
                    var sdList = new SiteDriveList(mission, pipeline, (_, id) => FilterWedge(id));
                    LoadList(sdList, url);
                    rdrDir = sdList.RDRDir; //might still be null if all wedges were filtered
                }
                if (rdrDir == null)
                {
                    pipeline.LogWarn("failed to parse RDR dir from URL {0}", url);
                    return true; //drop message
                }
                var sd = GetSiteDrive(url);
                if (!sd.HasValue)
                {
                    pipeline.LogWarn("failed to parse site drive from URL {0}", url);
                    return true; //drop message
                }
                lock (changedURLs)
                {
                    if (!changedURLs.ContainsKey(rdrDir))
                    {
                        changedURLs[rdrDir] = new Dictionary<SiteDrive, Dictionary<string, long>>();
                    }
                    if (!changedURLs[rdrDir].ContainsKey(sd.Value))
                    {
                        changedURLs[rdrDir][sd.Value] = new Dictionary<string, long>();
                    }
                    changedURLs[rdrDir][sd.Value][url] = (long)UTCTime.NowMS();
                }
                return true; //successfully processed, remove message from queue
            }
            else if (msg is ContextualMeshMessage)
            {
                var parameters = MakeParameters(msg as ContextualMeshMessage);
                if (parameters != null)
                {
                    BuildContextualTileset(parameters); //throws exception on error or if killed
                }
                return true; //message ignored or successfully processed, remove from queue
            }
            else
            {
                pipeline.LogWarn("unknown message type {0}, dropping message", msg.GetType().Name);
                return true;
            }
        }

        protected override bool ParseArguments()
        {
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
                if (!string.IsNullOrEmpty(options.ListPattern) &&
                    !string.Equals(options.ListPattern, "none", StringComparison.OrdinalIgnoreCase))
                {
                    listRegex = StringHelper.WildcardToRegularExpression(options.ListPattern, RegexOptions.IgnoreCase);
                }

                if (!string.IsNullOrEmpty(options.WedgePattern) &&
                    !string.Equals(options.WedgePattern, "none", StringComparison.OrdinalIgnoreCase))
                {
                    wedgeRegex =
                        StringHelper.WildcardToRegularExpression(options.WedgePattern, RegexOptions.IgnoreCase);
                }

                if (!string.IsNullOrEmpty(options.TexturePattern) &&
                    !string.Equals(options.TexturePattern, "none", StringComparison.OrdinalIgnoreCase))
                {
                    textureRegex =
                        StringHelper.WildcardToRegularExpression(options.TexturePattern, RegexOptions.IgnoreCase);
                }

                if (!serviceUtilMode || options.DeleteQueues)
                {
                    if (string.IsNullOrEmpty(options.WorkerQueueName) && !options.DeleteQueues)
                    {
                        throw new Exception("--workerqueuename required with --master");
                    }
                    workerQueue = GetWorkerMessageQueue();
                }
            }

            debounceMS = 1000 * (options.MasterDebounceSec >= 0 ? options.MasterDebounceSec : DEF_DEBOUNCE_SEC);
            pipeline.LogInfo("debounce time {0}s", debounceMS / 1000);

            solRange = options.MaxSolRange >= 0 ? options.MaxSolRange : DEF_MAX_SOL_RANGE;
            maxWedges = options.MaxContextualMeshWedges > 0 ? options.MaxContextualMeshWedges : int.MaxValue;
            maxSDs = options.MaxSiteDrives > 0 ? options.MaxSiteDrives : int.MaxValue;
            pipeline.LogInfo("contextual mesh extent {0}, surface extent {1}", options.Extent, options.SurfaceExtent);
            pipeline.LogInfo("max sol range {0}, max wedges {1}, max sitedrives {2}", solRange, maxWedges, maxSDs);
            pipeline.LogInfo("min wedges for primary sitedrive {0}", options.MinPrimarySiteDriveWedges);

            return true;
        }

        protected override void RefreshCredentials()
        {
            base.RefreshCredentials();

            if (workerQueue != null)
            {
                workerQueue = GetWorkerMessageQueue();
            }
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

        protected override string GetSubcommandConfigFolder()
        {
            return "contextual-subcommands";
        }

        protected override string GetSubcommandCacheDir()
        {
            return "contextual";
        }

        protected override void DeleteQueues()
        {
            base.DeleteQueues();
            DeleteQueue(workerQueue, "worker"); //null ok
        }

        protected override void RunService()
        {
            if (options.Master)
            {
                Task.Run(() => MasterLoop());
            }
            base.RunService();
        }

        private string MakeSolRanges(HashSet<int> sols, int primarySol = -1)
        {
            var ranges = new List<int[]>();
            var skipped = new List<int>();
            foreach (var sol in sols.OrderBy(sol => sol))
            {
                if (primarySol >= 0 && Math.Abs(sol - primarySol) > solRange)
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
                                 skipped.Count, solRange, primarySol, string.Join(", ", skipped));
            }
            return String.Join(",", ranges.Select(range => range[0] + (range[0] != range[1] ? ("-" + range[1]) : "")));
        }

        private ContextualMeshParameters MakeParameters(ContextualMeshMessage msg)
        {
            if (msg.primarySol < 0 || string.IsNullOrEmpty(msg.primarySiteDrive))
            {
                return null;
            }

            var ret = new ContextualMeshParameters();

            ret.RDRDir = !string.IsNullOrEmpty(msg.rdrDir) ? msg.rdrDir : options.RDRDir;
            
            ret.PrimarySol = msg.primarySol;
            ret.Sols.Add(ret.PrimarySol);
            
            if (!string.IsNullOrEmpty(msg.sols))
            {
                ret.Sols.UnionWith(IngestAlignmentInputs.ExpandSolSpecifier(msg.sols));
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
            int sep = sols.IndexOfAny(new char[] { ',', '-' });
            sep = sep < 0 ? sols.Length : sep;
            ret.PrimarySol = int.Parse(sols.Substring(0, sep));
            ret.Sols.UnionWith(IngestAlignmentInputs.ExpandSolSpecifier(sols));
            var sds = SiteDrive.ParseList(siteDrives);
            ret.PrimarySiteDrive = sds[0];
            ret.SiteDrives.UnionWith(sds);
            return ret;
        }

        private void BuildContextualTileset(ContextualMeshParameters p)
        {
            pipeline.LogInfo(Dump(p, verbose: true));
            BuildContextualTileset(p.RDRDir, p.PrimarySol, p.Sols, p.PrimarySiteDrive, p.SiteDrives);
        }

        /// <summary>
        /// rdrDir is e.g.
        /// * "s3://BUCKET/ods/VER/sol/#####/ids/rdr"
        /// * "s3://BUCKET/ods/VER/YYYY/###/ids/rdr"
        /// * "s3://BUCKET/foo/bar"
        /// * "c:/foo/bar"
        /// * "./foo/bar"
        /// * null -> use options.RDRDir
        /// </summary>
        private void BuildContextualTileset(string rdrDir, int primarySol, HashSet<int> sols,
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

            string missionStr = mission != null ? mission.GetMission().ToString() : "None";
            string fullMissionStr = mission != null ? mission.GetMissionWithVenue() : "None";
            string sdStr = primarySiteDrive.ToString();
            string solStr = SolToString(primarySol, forceNumeric: true);
            string sdsStr = string.Join(",", siteDrives.ToArray());
            string solRanges = MakeSolRanges(sols, primarySol);
            string project = string.Format("{0}_{1}", SolToString(primarySol), sdStr);
            string venue = string.Format("contextual_{0}_{1}", missionStr, project);
            string venueDir = storageDir + "/" + venue;
            string solDir = StringHelper.ReplaceIntWildcards(rdrDir, primarySol);
            string ingestDir = solDir;
            string fetchDir = !string.IsNullOrEmpty(options.FetchDir) ? options.FetchDir : storageDir + "/" + FETCH_DIR;
            string tilesetDir = GetTilesetDir(venue, sdStr, project);
            string destDir = GetDestDir(solDir);

            var orbitalCfg = OrbitalConfig.Instance;
            var orbitalDir = fetchDir + "/orbital/" + missionStr + "/";
            Func<string, string, string> oopt =
                (opt, cfg) => StringHelper.NormalizeSlashes(!string.IsNullOrEmpty(opt) ? opt : cfg);
            string orbitalDEMUrl = oopt(options.OrbitalDEMURL, orbitalCfg.DEMURL);
            string orbitalImageUrl = oopt(options.OrbitalImageURL, orbitalCfg.ImageURL);
            string orbitalDEMFile = oopt(options.OrbitalDEM, orbitalCfg.GetDEMFile(orbitalDir, orbitalDEMUrl));
            string orbitalImageFile = oopt(options.OrbitalImage, orbitalCfg.GetImageFile(orbitalDir, orbitalImageUrl));
            string noOrbital = "";
            if (options.NoOrbital || (string.IsNullOrEmpty(orbitalDEMFile) && string.IsNullOrEmpty(orbitalImageFile)))
            {
                noOrbital = "--noorbital";
            }
            string noSurface = options.NoSurface ? "--nosurface" : "";

            string orbitalDEMFileOpt = !string.IsNullOrEmpty(orbitalDEMFile) ? $"--orbitaldem={orbitalDEMFile}" : null;

            string orbitalImageFileOpt =
                !string.IsNullOrEmpty(orbitalImageFile) ? $"--orbitalimage={orbitalImageFile}" : null;

            string camerasOpt =
                !string.IsNullOrEmpty(options.OnlyForCameras) ? $"--onlyforcameras={options.OnlyForCameras}" : null;

            string allowUnmasked = options.AllowUnmaskedRoverObservations ? "--allowunmaskedroverobservations" : null;

            pipeline.LogInfo("building contextual tileset {0} from {1} sitedrives in {2} sols",
                             project, siteDrives.Count, sols.Count);
            try
            {
                Cleanup(venueDir);

                Configure(venue);

                if (!options.NoFetch && rdrDir.StartsWith("s3://") && !(pipeline is CloudPipeline))
                {
                    ingestDir = fetchDir + "/rdrs";
                    Fetch(options.MaxFetch, solRanges, ingestDir, rdrDir,
                          "--onlyforsitedrives", sdsStr, "--nomeshes", "--summary");
                }

                if (!options.NoFetch && !options.NoOrbital)
                {
                    Action<string, string> fetchOrbitalAsset = (url, file) =>
                    {
                        if (!string.IsNullOrEmpty(url) && !string.IsNullOrEmpty(file))
                        {
                            if (!File.Exists(file))
                            {
                                pipeline.LogInfo("fetching orbital asset {0} -> {1}", url, file);
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
                            else
                            {
                                pipeline.LogInfo("using cached orbital asset {0} -> {1}", url, file);
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
                    RunCommand("ingest", project, "--mission", fullMissionStr, "--onlyforsitedrives", sdsStr,
                               "--onlyforsols", solRanges,
                               "--inputpath", ingestDir + "/" + (options.RecursiveSearch ? "**" : "*"), noSurface,
                               noOrbital, "--orbitalframe", sdStr, orbitalDEMFileOpt, orbitalImageFileOpt, camerasOpt);
                }

                if (!options.NoTileset)
                {
                    RunCommand("bev-align", options.AbortOnAlignmentError, project, "--fixsitedrives", sdStr,
                               allowUnmasked);

                    RunCommand("heightmap-align", options.AbortOnAlignmentError, project, "--basesitedrive", sdStr,
                               allowUnmasked);
                    
                    RunCommand("build-geometry", project, "--meshframe", sdStr, "--extent", options.Extent.ToString(),
                               "--surfaceextent", options.SurfaceExtent.ToString(), allowUnmasked);

                    RunCommand("build-tiling-input", project, "--meshframe", sdStr, allowUnmasked);
                    
                    RunCommand("blend-images", project, "--meshframe", sdStr, allowUnmasked);
                    
                    BuildTileset(project, "--meshframe", sdStr, allowUnmasked);
                    
                    RunCommand("update-scene-manifest", project, "--notactical", "--nourls", "--nosky", allowUnmasked,
                               "--sol", solStr, "--sitedrive", sdStr, "--manifestfile", tilesetDir + "/" + SCENE_JSON);

                    SaveTileset(tilesetDir, project, destDir);

                    if (!options.NoSky)
                    {
                        RunCommand("build-sky-sphere", project, "--meshframe", sdStr,
                                   "--skymode", options.SkyMode.ToString(), allowUnmasked,
                                   "--skysphereradius", options.SkySphereRadius,
                                   "--skyminbackprojectradius", options.SkyMinBackprojectRadius);
                        string skyTilesetDir = GetTilesetDir(venue, sdStr, project, BuildSkySphere.SKY_TILESET_DIR);
                        SaveTileset(skyTilesetDir, project, destDir, "_sky");
                    }
                }

                if (!options.NoCombinedManifest)
                {
                    RunCommand("update-scene-manifest", project, allowUnmasked, "--tilesetdir", destDir,
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

        private string FilterWedge(RoverProductId id)
        {
            if (!(id is OPGSProductId))
            {
                return "unrecognized product ID";
            }
            if ((id as OPGSProductId).Size == RoverProductSize.Thumbnail)
            {
                return "thumbnail product";
            }
            //disabling this check - some missions might actually put e.g. RAS products in the list files.
            //Consider that MSL unified meshes have RAS IDs in them, I think.
            //if (!RoverProduct.IsGeometry(id.ProductType))
            //{
            //    return "not a geometry product";
            //}
            if (mission != null)
            {
                if (!mission.CheckProductId(id, out string reason))
                {
                    return reason;
                }
                if (!mission.UseForMeshing(id))
                {
                    return "product type not used for meshing";
                }
                var preferredEye = mission.PreferEyeForGeometry();
                if (!RoverStereoPair.IsStereoEye(id.Camera, preferredEye))
                {
                    return string.Format("stereo eye {0} != {1}", id.Camera, preferredEye);
                }
                var preferredGeometry = mission.PreferLinearGeometryProducts() ?
                    RoverProductGeometry.Linearized : RoverProductGeometry.Raw;
                if (id.Geometry != preferredGeometry)
                {
                    return string.Format("linearity {0} != {1}", id.Geometry, preferredGeometry);
                }
            }
            return null;
        }

        private string FilterTexture(RoverProductId id)
        {
            if (!(id is OPGSProductId))
            {
                return "unrecognized product ID";
            }
            if ((id as OPGSProductId).Size == RoverProductSize.Thumbnail)
            {
                return "thumbnail product";
            }
            if (mission != null)
            {
                if (!mission.CheckProductId(id, out string reason))
                {
                    return reason;
                }
                if (!mission.UseForTexturing(id))
                {
                    return "product type not used for texturing";
                }
                var preferredGeometry = mission.PreferLinearRasterProducts() ?
                    RoverProductGeometry.Linearized : RoverProductGeometry.Raw;
                if (id.Geometry != preferredGeometry)
                {
                    return string.Format("linearity {0} != {1}", id.Geometry, preferredGeometry);
                }
            }
            return null;
        }

        //RRR_[TTTT]SSSDDDD[I][_VV].lis
        //where RRR is e.g. xyz, xym, iv, etc; TTTT is sol; SSS site; DDDD drive; I instrument; VV version
        private static readonly Regex LIST_FILENAME_REGEX =
            new Regex(@"[^_]+_(?:\d{4})?([0-9A-Z]{7})[A-Z]?(?:_[0-9A-Z]{1,2})?\.lis$", RegexOptions.IgnoreCase);

        private SiteDrive? GetSiteDrive(string url)
        {
            var idStr = StringHelper.GetLastUrlPathSegment(url, stripExtension: true);
            var id = RoverProductId.Parse(idStr, mission, throwOnFail: false);
            if (id is OPGSProductId)
            {
                return (id as OPGSProductId).SiteDrive;
            }
            var m = LIST_FILENAME_REGEX.Match(url);
            if (m.Success)
            {
                return new SiteDrive(m.Groups[1].Value);
            }
            return null;
        }

        private void LoadList(SiteDriveList sdList, String url)
        {
            int split = url.LastIndexOf("/ids-pipeline/");
            string baseUrl = split > 0 ? url.Substring(0, split) : ("s3://" + (new S3Url(url).BucketName));
            sdList.LoadListFile(GetFile(url), baseUrl);
        }

        private Dictionary<SiteDrive, Stamped<SiteDriveList>>
            LoadSiteDriveLists(string rdrDir, Dictionary<SiteDrive, Dictionary<string, long>> urls)
        {
            rdrDir = StringHelper.NormalizeUrl(rdrDir, preserveTrailingSlash: false) + "/"; //prob redundant, but ok

            pipeline.LogInfo("loading sitedrive lists for RDR dir {0}", rdrDir);

            var ret = new Dictionary<SiteDrive, Stamped<SiteDriveList>>();

            var listDirs = new HashSet<string>();
            var listURLs = new HashSet<string>();
            var wedgeURLs = new HashSet<string>();
            var textureURLs = new HashSet<string>();

            SiteDriveList makeList(SiteDrive sd)
            {
                return new SiteDriveList(rdrDir, sd, mission, pipeline, (url, id) => FilterWedge(id));
            }

            //keep only latest version of each product
            //remove non-preferred stereo eye
            //remove non-preferred lin/nonlin
            //etc
            void filterLists(bool passthroughEmpty)
            {
                pipeline.LogInfo("filtering {0} sitedrive wedge lists", ret.Count);
                var filtered = new Dictionary<SiteDrive, Stamped<SiteDriveList>>();
                foreach (var sd in ret.Keys)
                {
                    if (ret[sd].Value.NumWedges > 0)
                    {
                        var sdList = ret[sd].Value
                            .FilterProductIDs(ids => RoverObservationComparator.FilterProductIdGroups(ids, mission));
                        if (sdList.NumWedges > 0)
                        {
                            filtered[sd] = new Stamped<SiteDriveList>(sdList, ret[sd].Timestamp);
                        }
                    }
                    else if (passthroughEmpty)
                    {
                        filtered[sd] = ret[sd];
                    }
                }
                int culled = ret.Count - filtered.Count;
                ret = filtered;
                if (culled > 0)
                {
                    pipeline.LogInfo("culled {0} sitedrive wedge lists that were empty after filtering, {1} remain",
                                     culled, ret.Count);
                }
            }

            foreach (var sd in urls.Keys)
            {
                var sdList = makeList(sd);
                long latestTimestamp = -1;
                foreach (var entry in urls[sd])
                {
                    string url = entry.Key;
                    if (!FileExists(url))
                    {
                        pipeline.LogWarn("file {0} not found", url);
                        continue;
                    }
                    string file = StringHelper.GetLastUrlPathSegment(url);
                    if (listRegex != null && listRegex.IsMatch(file))
                    {
                        LoadList(sdList, url);
                        listDirs.Add(StringHelper.StripLastUrlPathSegment(url) + "/");
                        listURLs.Add(url);
                    }
                    else if (wedgeRegex != null && wedgeRegex.IsMatch(file))
                    {
                        sdList.Add(url);
                        wedgeURLs.Add(url);
                    }
                    else if (textureRegex != null && textureRegex.IsMatch(file))
                    {
                        sdList.Add(url);
                        textureURLs.Add(url);
                    }
                    else //URL should have been rejected by AcceptMessage(), but whatever
                    {
                        pipeline.LogWarn("unhandled file type: {0}", url);
                    }
                    latestTimestamp = Math.Max(latestTimestamp, entry.Value);
                }
                if (sdList.NumSols > 0) //not NumWedges to handle case of only texture  updates
                {
                    ret[sd] = new Stamped<SiteDriveList>(sdList, latestTimestamp);
                }
            }

            pipeline.LogInfo("registered {0} changed lists in {1} dirs", listURLs.Count, listDirs.Count);
            pipeline.LogInfo("registered {0} changed wedges, {1} changed textures", wedgeURLs.Count, textureURLs.Count);

            //at this point lists that were created only due to texture changes will exist but have no wedges
            //but wedges may be added to them if we search for and find additional lists or wedges below
            //this is important because it catches situations where XYZs are acquired for a sitedrive in sols up to N
            //but then in later sols M > N only additional textures are acquired for that sitedrive
            filterLists(passthroughEmpty: true);

            if (pipeline.Verbose)
            {
                foreach (var sd in ret.Keys.OrderBy(sd => sd))
                {
                    pipeline.LogVerbose("changed sitedrive {0}: {1} sols, {2} wedges before additions{3}{4}",
                                        sd, ret[sd].Value.NumSols, ret[sd].Value.NumWedges,
                                        ret[sd].Value.NumWedges > 0 ? ":\n  " : "",
                                        string.Join("\n  ", ret[sd].Value.IDToURL.Values.OrderBy(url => url)));
                }
            }

            int minPrimarySol = ret.Values.Select(sl => sl.Value.MinSol).Min();
            int maxPrimarySol = ret.Values.Select(sl => sl.Value.MaxSol).Max();
            int minSol = Math.Max(0, minPrimarySol - solRange), maxSol = maxPrimarySol + solRange;

            pipeline.LogInfo("primary sol range {0}-{1}, sol search range {2}-{3}",
                             minPrimarySol, maxPrimarySol, minSol, maxSol);

            bool reFilter = false;

            if (!options.NoSearchForAdditionalLists && listRegex != null) //handle options.ListPattern=none
            {
                int additionalLists = 0, additionalListSitedrives = 0;

                foreach (var listDir in listDirs)
                {
                    pipeline.LogInfo("searching for additional list files in {0}", listDir);
                    foreach (var listFile in SearchFiles(listDir, "*/" + options.ListPattern, recursive: false))
                    {
                        string url = StringHelper.NormalizeUrl(listFile);
                        if (!listURLs.Contains(url))
                        {
                            listURLs.Add(url);
                            var sd = GetSiteDrive(url);
                            if (sd.HasValue)
                            {
                                pipeline.LogVerbose("found additional list {0} in sitedrive {1}", url, sd);
                                additionalLists++;
                                var sdList = ret.ContainsKey(sd.Value) ? ret[sd.Value].Value : makeList(sd.Value);
                                LoadList(sdList, url);
                                sdList = sdList.FilterToSolRange(minSol, maxSol);
                                if (ret.ContainsKey(sd.Value))
                                {
                                    ret[sd.Value] = new Stamped<SiteDriveList>(sdList, ret[sd.Value].Timestamp);
                                }
                                else if (sdList.NumWedges > 0)
                                {
                                    ret[sd.Value] = new Stamped<SiteDriveList>(sdList);
                                    additionalListSitedrives++;
                                }
                            }
                            else
                            {
                                pipeline.LogWarn("failed to parse site drive from URL {0}", url);
                            }
                        }
                    }
                }

                reFilter |= additionalLists > 0;

                pipeline.LogInfo("loaded {0} additional lists ({1} additional sitedrives)",
                                 additionalLists, additionalListSitedrives);
            }

            if (!options.NoSearchForAdditionalWedges && wedgeRegex != null) //handle options.WedgePattern=none
            {
                int additionalWedges = 0, additionalWedgeSitedrives = 0;

                var solDirs = new List<string>();
                if (SiteDriveList.GetSolSpan(rdrDir, out int start, out int len))
                {
                    string dir = rdrDir.Substring(0, start); //includes trailing slash
                    foreach (var url in storageHelper.SearchObjects(dir, recursive: false, folders: true, files: false))
                    {
                        string solStr = StringHelper.GetLastUrlPathSegment(url.TrimEnd('/'));
                        if (int.TryParse(solStr, out int sol) && sol >= minSol && sol <= maxSol)
                        {
                            solDirs.Add(url);
                        }
                    }
                    pipeline.LogInfo("recursively searching {0} sol directories for additional wedges", solDirs.Count);
                }
                else
                {
                    pipeline.LogWarn("could not find sol span, recursively searching RDR dir {0} for additional wedges",
                                     rdrDir);
                    solDirs.Add(rdrDir);
                }

                foreach (string dir in solDirs)
                {
                    foreach (var wdg in storageHelper.SearchObjects(dir, "*/" + options.WedgePattern, recursive: true))
                    {
                        string url = StringHelper.NormalizeUrl(wdg);
                        if (!wedgeURLs.Contains(url) && AcceptBucketPath(url))
                        {
                            wedgeURLs.Add(url);
                            var idStr = StringHelper.GetLastUrlPathSegment(url, stripExtension: true);
                            var id = RoverProductId.Parse(idStr, mission, throwOnFail: false);
                            if (FilterWedge(id) == null)
                            {
                                var sd = (id as OPGSProductId).SiteDrive;
                                var sdl = ret.ContainsKey(sd) ? ret[sd].Value : makeList(sd);
                                int nw = sdl.NumWedges;
                                if (sdl.Add(url) == null && sdl.NumWedges > nw) //not rejected and not duplicate
                                {
                                    //this new URL might still get filtered out below
                                    //e.g. if it's an older version of something that's already in the list
                                    pipeline.LogVerbose("found additional wedge {0} in sitedrive {1}", url, sd);
                                    additionalWedges++;
                                    if (!ret.ContainsKey(sd))
                                    {
                                        ret[sd] = new Stamped<SiteDriveList>(sdl);
                                        additionalWedgeSitedrives++;
                                    }
                                }
                            }
                        }
                    }
                }

                reFilter |= additionalWedges > 0;

                pipeline.LogInfo("found {0} additional wedge products ({1} additional sitedrives)",
                                 additionalWedges, additionalWedgeSitedrives);
            }

            if (reFilter)
            {
                filterLists(passthroughEmpty: false);
            }

            pipeline.LogInfo("{0} filtered sitedrive lists for RDR dir {1}", ret.Count, rdrDir);

            var changedSDs = new HashSet<SiteDrive>(urls.Keys.Where(sd => ret.ContainsKey(sd)).ToList());

            if (pipeline.Verbose)
            {
                foreach (var sd in ret.Keys.OrderBy(sd => sd))
                {
                    pipeline.LogVerbose("{0}changed sitedrive {1}: {2} sols, {3} wedges after additions{4}{5}",
                                        changedSDs.Contains(sd) ? "" : "un", sd, ret[sd].Value.NumSols,
                                        ret[sd].Value.NumWedges, ret[sd].Value.NumWedges > 0 ? ":\n  " : "",
                                        string.Join("\n  ", ret[sd].Value.IDToURL.Values.OrderBy(url => url)));
                }
            }

            pipeline.LogInfo("{0} filtered changed sitedrives: {1}", changedSDs.Count, String.Join(",", changedSDs));

            return ret;
        }

        /// <summary>
        /// Applys heruistics to possibly make a ContextualMesh for the sitedrive of primarySDList.
        /// Returns null if it decided not to make one, or if there was a problem.
        /// If placesDB is null only the primary sitedrive is included unless options.MaxSiteDriveDistance <= 0.
        /// Similarly, if options.MaxSiteDriveDistance > 0 and PlacesDB fails to return an offset for any sitedrive
        /// other than the primary then that sitedrive will not be included.
        /// Otherwise considers additional sitedrives from sdLists, which should all have same RDRDir as primarySDList.
        /// </summary>
        private ContextualMeshMessage SiteDriveChanged(SiteDriveList primarySDList, List<SiteDriveList> sdLists,
                                                       PlacesDB placesDB)
        {
            string rdrDir = primarySDList.RDRDir;
            int primarySol = primarySDList.MaxSol;
            SiteDrive primarySD = primarySDList.SiteDrive;

            string name = string.Format("{0}_{1}", SolToString(primarySol), primarySD.ToString());

            double maxDistance = options.MaxSiteDriveDistance;

            int minSol = primarySol - solRange;
            int maxSol = primarySol + solRange;
            primarySDList = primarySDList.FilterToSolRange(minSol, maxSol);

            //primary site drive must have at least this many wedges
            if (options.MinPrimarySiteDriveWedges > 0 && primarySDList.NumWedges < options.MinPrimarySiteDriveWedges)
            {
                pipeline.LogInfo("not producing contextual mesh {0} in {1}, {2} < {3} wedges",
                                 name, rdrDir, primarySDList.NumWedges, options.MinPrimarySiteDriveWedges);
                return null;
            }

            var keepers = new Dictionary<SiteDrive, SiteDriveList>();
            keepers[primarySD] = primarySDList;

            var distance = new Dictionary<SiteDrive, double>();

            foreach (var list in sdLists)
            {
                if (list.RDRDir != rdrDir)
                {
                    pipeline.LogWarn("not including sitedrive {0} in contextual mesh for {1}, RDR dir {2} != {3}",
                                     list.SiteDrive, primarySD, list.RDRDir, rdrDir);
                    continue;
                }
                if (!keepers.ContainsKey(list.SiteDrive))
                {
                    var filtered = list.FilterToSolRange(minSol, maxSol);
                    if (filtered.NumSols > 0)
                    {
                        if (maxDistance <= 0)
                        {
                            keepers[list.SiteDrive] = filtered;
                        }
                        else if (placesDB != null)
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
                                pipeline.LogException
                                    (ex, $"not including sitedrive {list.SiteDrive} in contextual mesh " +
                                     "for {primarySD}: error checking distance <= {maxDistance} with PlacesDB");
                            }
                        }
                        else
                        {
                            pipeline.LogWarn("not including sitedrive {0} in contextual mesh for {1}, " +
                                             "PlacesDB not available to check distance <= {2}",
                                             list.SiteDrive, primarySD, maxDistance);
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
                    .Where(l => l.SiteDrive != primarySD) //never cull the primary sitedrive
                    .Where(l => l.NumWedges > 0)
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

            var sols = new HashSet<int>();
            sols.UnionWith(keepers.Values.SelectMany(l => l.Sols));

            return new ContextualMeshMessage()
            {
                rdrDir = rdrDir,
                primarySol = primarySol,
                primarySiteDrive = primarySD.ToString(),
                sols = MakeSolRanges(sols, primarySol),
                siteDrives = string.Join(",", keepers.Keys.OrderBy(sd => sd)),
                numWedges = totalWedges,
                timestamp = (long)UTCTime.NowMS()
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

                    //RDR directory -> sitedrive -> list or wedge URL -> last changed UTC milliseconds
                    var urlsToProcess = new Dictionary<string, Dictionary<SiteDrive, Dictionary<string, long>>>();
                    lock (changedURLs)
                    {
                        foreach (string rdrDir in changedURLs.Keys)
                        {
                            long lastChange = changedURLs[rdrDir].Values
                                .SelectMany(d => d.Values)
                                .DefaultIfEmpty(-1)
                                .Max();
                            if (lastChange >= 0 && (debounceMS <= 0 || lastChange <= (now - debounceMS)))
                            {
                                urlsToProcess[rdrDir] = changedURLs[rdrDir];
                                pipeline.LogInfo("processing RDR dir {0} with {1} changed sitedrives, " +
                                                 "last change at {2}, {3:F3}s debounce",
                                                 rdrDir, urlsToProcess[rdrDir].Count, ToLocalTime(lastChange),
                                                 debounceMS / 1000);

                            }
                        }
                        foreach (string rdrDir in urlsToProcess.Keys)
                        {
                            changedURLs.Remove(rdrDir);
                        }
                    }
                    
                    foreach (var rdrDir in urlsToProcess.Keys)
                    {
                        var sdLists = LoadSiteDriveLists(rdrDir, urlsToProcess[rdrDir]);

                        if (sdLists.Count < 1)
                        {
                            continue;
                        }

                        var msgs = new List<Stamped<ContextualMeshMessage>>();

                        //try to connect to PlacesDB just for this pass
                        //we do that for a couple of reasons rather than having a single long-lived PlacesDB connection
                        //for one thing our PlacesDB interface caches results, and the cache could become stale
                        //also, particularly in certain dev scenarios, PlacesDB availability may be iffy
                        //better to try on each pass rather than once ever
                        var placesCfg = PlacesConfig.Instance;
                        bool usePlaces = !string.IsNullOrEmpty(placesCfg.Url) && !string.IsNullOrEmpty(placesCfg.View);
                        lock (usePlaces ? credentialRefreshLock : new Object())
                        {
                            PlacesDB placesDB = null;
                            if (usePlaces)
                            {
                                try
                                {
                                    placesDB = new PlacesDB(pipeline);
                                    pipeline.LogInfo("using PlacesDB " + placesCfg.Url);
                                }
                                catch (Exception ex)
                                {
                                    pipeline.LogError("error initializing PlacesDB: {0}", ex.Message);
                                }
                            }
                            if (placesDB == null)
                            {
                                pipeline.LogInfo("not using PlacesDB");
                            }

                            var allSDs = sdLists.Values.Select(sl => sl.Value).ToList();
                            foreach (var changedSD in urlsToProcess[rdrDir].Keys.Where(sd => sdLists.ContainsKey(sd)))
                            {
                                try
                                {
                                    var msg = SiteDriveChanged(sdLists[changedSD].Value, allSDs, placesDB);
                                    if (msg != null)
                                    {
                                        msgs.Add(new Stamped<ContextualMeshMessage>(msg, sdLists[changedSD].Timestamp));
                                    }
                                }
                                catch (Exception ex)
                                {
                                    pipeline.LogException(ex, "error processing sitedrive " + changedSD);
                                }
                            }
                        }

                        //order messages according to when the corresponding sitedrive list changed (oldest to newest)
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

                    if (urlsToProcess.Count > 0)
                    {
                        pipeline.DeleteDownloadCache();
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
