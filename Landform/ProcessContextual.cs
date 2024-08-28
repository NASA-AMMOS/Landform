using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using CommandLine;
using JPLOPS.Util;
using JPLOPS.Cloud;
using JPLOPS.Pipeline;
using JPLOPS.Geometry;
using JPLOPS.Pipeline.AlignmentServer;

//RDR directory -> sitedrive -> list or wedge URL -> last changed UTC milliseconds
using DictionaryOfChangedURLs =
    System.Collections.Generic.Dictionary<string,
        System.Collections.Generic.Dictionary<JPLOPS.Pipeline.SiteDrive,
            System.Collections.Generic.Dictionary<string, long>>>;

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
/// 10. update-scene-manifest (optional combined manifest for the scene with absolute URLs)
///
/// As a service, process-contextual is designed to run over a long period of time, receiving messages on an SQS queue,
/// creating contextual meshes, and uploading them back to S3.
///
/// As a command line tool, process-contextual can be used to build individual contextual mesh tilesets.  It can either
/// operate entirely locally, reading from and writing to disk, or it can read from and write to S3.
///
/// Also see ProcessTactical.cs which automates the tactical mesh tileset workflow.
///
/// ProcessContextual currently only works with OPGS product IDs.  Support for MSL MSSS product IDs is TODO.
///
/// A contextual mesh is generated for a specific primary sol and primary sitedrive.  It combines data from a set of
/// sols and sitedrives (which must contain the primary sol/sitedrive), as well as orbital assets if available.
/// PlacesDB is required to combine orbital and surface data.
///
/// Orbital-only contextual meshes, i.e. "orbital meshes", can be created by specifying --nosurface and/or by setting a
/// corresponding flag in incoming messages when running as a service.
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
/// The output tileset is named TTTT_SSSDDDD[Vnn][_orbital] where TTTT is the primary sol and SSSDDDD is the primary
/// sitedrive.  Vnn is an optional version suffix where V is a literal V and nn is typically a two digit positive
/// integer, but can be any string not containing whitespace, slashes, or underscores.  It is written to
/// rdrDir/tileset/TTTT_SSSDDDD[Vnn][_orbital] (*), unless --outputfolder is specified, in which case it is written to a
/// subdirectory TTTT_SSSDDDD[Vnn][_orbital] there.
///
/// (*) Actually if rdrDir contains a prefix ending /rdr then the output directory is that prefix but with rdr replaced
/// with rdr/tileset/TTTT_SSSDDDD[Vnn][_orbital].
///
/// When run as a service the RDR directory is also given as part of each SQS message.  Thus, the service will write the
/// tilesets back to the same RDR tree as the source RDRs, but under the rdr/tileset subdirectory. (If the source bucket
/// is in the list of --readonlybuckets then the generated tileset will be written to the alternate location given by
/// --readonlybucketaltdest.)
///
/// The tileset will contain
/// * one .b3dm file per tile
/// * a tilest file TTTT_SSSDDDD[Vnn][_orbital]/TTTT_SSSDDDD[Vnn][_orbital]_tileset.json
/// * a manifest file TTTT_SSSDDDD[Vnn][_orbital]/TTTT_SSSDDDD[Vnn][_orbital]_scene.json with relative URLs
/// * a stats file TTTT_SSSDDDD[Vnn][_orbital]/TTTT_SSSDDDD[Vnn][_orbital]_stats.txt.
/// 
/// Unless --nosky is specified all of the above also holds for a sky tileset named TTTT_SSSDDDD[Vnn]_sky.
/// 
/// A combined scene manifest TTT_SSSDDDD[Vnn][_orbital]_scene.json with absolute URLs can also be optionally created or
/// updated as a sibling of the output tileset directory.  In that case the update-scene-manifest tool will also include
/// any sibling tactical mesh tilesets in the manifest.
///
/// This service can also run in master service mode by specifying --master.  In that mode the service listens for
/// messages indicating XYZ list, XYZ wedge files, wedge texture files, or FDR files have been created or updated.  Once
/// changes have stopped for at least a minimum debounce period or an EOP or EOX message has been received, the master
/// optionally scans for other list and/or wedge files and uses them to develop lists of changed sitedrives, available
/// sitedrives, and the number of wedges in each sitedrive.  Using PlacesDB to determine distances between sitedrives,
/// it may then produce one or more contextual mesh messages based on various parameters which limit the minimum size of
/// a sitedrive for which a contextual mesh is built, the maximum range of sols for which to include adjacent
/// sitedrives, the maximum distance for adjacent sitedrives, and the maximum number of wedges to include in a
/// contextual mesh.  If PlacesDB is not available then contextual meshes will only be built for single sitedrives, and
/// cannot combine both orbital and surface data.
///
/// Unless --norbital is specified, in master service mode orbital-only contextual meshes will also be built once
/// changes to FDR files have stopped for at least a minimum debounce period or an EOP, EOX, or EOF message has been
/// received.  The orbital mesh worker pool may be different from the contextual mesh worker pool.
///
/// By default no management of worker instances is performed.  It is possible to externally manage workers, e.g. by
/// permanently or manually instantiating them, or with an AWS auto scale group configured to launch workers when SQS
/// messages are available in the worker queues.  There are separate queues and thus separate worker pools for
/// orbital-only vs regular contextual meshes.  Master option --autostartworkers enables one of several forms of active
/// worker management.  If --[orbital]workerinstances is a list of one or more EC2 instance IDs or name patterns, the
/// corresponding workers are started (if not already running) when messages are added to the corresponding queue.  If
/// --[orbital]workerinstances is a string of the form asg:<name>[:<size>] then the desired number of instances in that
/// autoscale group is set to the given size (default 1).  If --workerautostartsec is positive then an additional 
/// auto start timer is activated that ensures the workers are running (in the same way as above) while messages are
/// available in the corresponding worker queue.  All of these options require permissions to perform the requisite
/// actions on AWS instances and/or auto scale groups.
///
/// The master never terminates or stops workers.  However, worker option --idleshutdownsec can be specified to enable
/// workers to go into a (permanent) idle state after a certain amount of inactivity (no messages available).  Option
/// --idleshutdownmethod then specifies what happens:
/// * None - no action, idle shutdown disabled (default)
/// * StopInstance - AWS EC2 StopInstance API is called to stop the worker
/// * StopInstanceOrShutdown - AWS EC2 StopInstance API is called to stop the worker, but if that fails, the worker
///   requests its OS to shutdown
/// * ScaleToZero - AWS auto scale API is called to set desired instances to 0 in the auto scale group named by option
///   --autoscalegroup
/// * LogIdle - The message "service idle, shutdown requested" is printed to the log.  This may then be detected by a
///   custom AWS log metric, which may then trigger a CloudWatch alarm, which may then trigger a scale-in of an
///   autoscale group.
/// * LogIdleProtected - Same as LogIdle, but the AWS EC2 API is used to enable scale-in protection at worker start, and
///   disable it when the worker becomes idle.
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
/// Landform.exe process-contextual --mission=M2020 --rdrdir=s3://bucket/ods/dev/sol/#####/ids/rdr
/// --sols=0281 --sitedrives=0160354 --outputfolder=out/g66bt-281
///
/// add --dryrun for dry run
/// add --notileset --nocombinedmanifest --nocleanup to just ingest and leave database
/// </summary>
namespace JPLOPS.Landform
{
    [Verb("process-contextual", HelpText = "process contextual meshes")]
    [EnvVar("CONTEXTUAL")]
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

        [Option(Default = null, HelpText = "Sitedrives with primary one first, mutually exclusive with --service, if omitted or \"auto\" then autodetect highest numbered sitedrive in primary sol and all other qualifying sitedrives in sol range")]
        public string SiteDrives { get; set; }

        [Option(Default = false, HelpText = "Don't fetch")]
        public bool NoFetch { get; set; }

        [Option(Default = null, HelpText = "Persistent download dir, defaults to \"fetched\" subdir of local Landform storage dir")]
        public string FetchDir { get; set; }

        [Option(Default = ProcessContextual.DEF_MAX_FETCH, HelpText = "Max fetched RDR bytes on disk, not including orbital, integer with optional case-insensitive suffix K,M,G, no limit if omitted or non-positive")]
        public string MaxFetch { get; set; }

        [Option(Default = ProcessContextual.DEF_MAX_ORBITAL, HelpText = "Max fetched orbital bytes on disk, integer with optional case-insensitive suffix K,M,G, no limit if empty or non-positive")]
        public string MaxOrbital { get; set; }

        [Option(Default = false, HelpText = "Don't ingest")]
        public bool NoIngest { get; set; }

        [Option(Default = false, HelpText = "Don't align")]
        public bool NoAlign { get; set; }

        [Option(Default = false, HelpText = "Don't build geometry")]
        public bool NoGeometry { get; set; }

        [Option(Default = false, HelpText = "Don't generate tileset")]
        public bool NoTileset { get; set; }

        [Option(Default = false, HelpText = "Don't generate sky sphere tileset")]
        public bool NoSky { get; set; }

        [Option(Default = ProcessContextual.Phase.begin, HelpText = "(Re)Start at phase: begin, fetch, ingest, align, geometry, leaves, blend, tileset, manifest, save, sky, combinedManifest (combine with --redo to ignore cached products)")]
        public ProcessContextual.Phase StartPhase { get; set; }

        [Option(Default = ProcessContextual.Phase.end, HelpText = "Stop at phase: fetch, ingest, align, geometry, leaves, blend, tileset, manifest, save, sky, combinedManifest, end (combine with --redo to ignore cached products)")]
        public ProcessContextual.Phase EndPhase { get; set; }

        [Option(HelpText = "Sky mode (Box, Sphere, TopoSphere, Auto)", Default = BuildSkySphere.DEF_SKY_MODE)]
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

        [Option(Default = false, HelpText = "Recreate existing orbital tilesets")]
        public bool RecreateExistingOrbital { get; set; }

        [Option(Default = false, HelpText = "Don't recreate existing contextual tilesets")]
        public bool NoRecreateExistingContextual { get; set; }

        [Option(Default = false, HelpText = "Coalesce existing orbital messages with new ones")]
        public bool CoalesceExistingOrbitalMessages { get; set; }

        [Option(Default = false, HelpText = "Coalesce existing contextual messages with new ones")]
        public bool CoalesceExistingContextualMessages { get; set; }

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

        [Option(Default = false, HelpText = "Don't ignore sol when comparing orbital tilesets")]
        public bool NoOrbitalCompareIgnoreSol { get; set; }

        [Option(Default = ProcessContextual.DEF_ORBITAL_CHECK_EXISTING_SOL_RANGE, HelpText = "If --noorbitalcompareignoresol is absent then check sols differing by at most this amount for existing orbital tilesets")]
        public int OrbitalCheckExistingSolRange { get; set; }

        [Option(Default = false, HelpText = "Abort contextual mesh workflow on unexpected error in an alignment stage")]
        public bool AbortOnAlignmentError { get; set; }

        [Option(Default = BuildGeometry.DEF_SURFACE_EXTENT, HelpText = "Surface geometry extent in meters.  This is typically just a minimum, and will typically be automatically expanded to fit the available surface data for each contextual mesh, up to a maximum limit.")]
        public double SurfaceExtent { get; set; }

        [Option(Default = BuildGeometry.DEF_EXTENT, HelpText = "Combined surface and orbital geometry extent in meters")]
        public double Extent { get; set; }

        [Option(Default = false, HelpText = "Don't check for extent overrides in SSM parameter store")]
        public bool NoAllowOverrideExtent { get; set; }

        [Option(Default = false, HelpText = "Run as contextual mesh master service")]
        public bool Master { get; set; }

        [Option(Default = null, HelpText = "Master service list filename pattern, case insensitive, e.g. xyz_*.lis, null, empty, or \"none\" to reject list files")]
        public string ListPattern { get; set; }

        [Option(Default = ProcessContextual.DEF_WEDGE_PATTERN, HelpText = "Master service wedge filename pattern, case insensitive, null, empty,or \"none\" to reject wedge files.  Extension must be .IMG, .VIC or .auto to use mission-specific preferred format.")]
        public string WedgePattern { get; set; }

        [Option(Default = ProcessContextual.DEF_TEXTURE_PATTERN, HelpText = "Master service texture filename pattern, case insensitive, null, empty,or \"none\" to reject texture files.  Extension must be .IMG, .VIC or .auto to use mission-specific preferred format.  \"mission\" will be replaced with the mission-specific image product type.")]
        public string TexturePattern { get; set; }

        [Option(Default = ProcessContextual.DEF_FDR_PATTERN, HelpText = "Master service FDR filename pattern for triggering orbital meshes, case insensitive, null, empty,or \"none\" to reject FDR files (in that case orbital meshes will be triggered on wedge and/or texture files).  Extension must be .IMG, .VIC or .auto to use mission-specific preferred format.")]
        public string FDRPattern { get; set; }

        [Option(Default = ProcessContextual.DEF_VCE_PATTERN, HelpText = "Master service VCE filename pattern, case insensitive, null, empty,or \"none\" to not filter out VCE files.")]
        public string VCEPattern { get; set; }

        [Option(Default = ProcessContextual.DEF_EOP_FILE_PATTERN, HelpText = "Master service end-of-processing file pattern, case insensitive, null, empty,or \"none\" to reject EOP files")]
        public string EOPFilePattern { get; set; }

        [Option(Default = ProcessContextual.DEF_EOP_MESSAGE_PATTERN, HelpText = "Master service end-of-processing message pattern, case insensitive, null, empty,or \"none\" to reject EOP messages")]
        public string EOPMessagePattern { get; set; }

        [Option(Default = ProcessContextual.DEF_EOF_FILE_PATTERN, HelpText = "Master service end-of-FDR file pattern, case insensitive, null, empty,or \"none\" to reject EOF files")]
        public string EOFFilePattern { get; set; }

        [Option(Default = ProcessContextual.DEF_EOF_MESSAGE_PATTERN, HelpText = "Master service end-of-FDR message pattern, case insensitive, null, empty,or \"none\" to reject EOF messages")]
        public string EOFMessagePattern { get; set; }

        [Option(Default = ProcessContextual.DEF_EOX_FILE_PATTERN, HelpText = "Master service end-of-XYZ file pattern, case insensitive, null, empty,or \"none\" to reject EOX files")]
        public string EOXFilePattern { get; set; }

        [Option(Default = ProcessContextual.DEF_EOX_MESSAGE_PATTERN, HelpText = "Master service end-of-XYZ message pattern, case insensitive, null, empty,or \"none\" to reject EOX messages")]
        public string EOXMessagePattern { get; set; }

        [Option(Default = false, HelpText = "If using list files (--listpattern is specified) then don't search for sibling list files to detect available sitedrives")]
        public bool NoSearchForAdditionalLists { get; set; }

        [Option(Default = false, HelpText = "If using wedge files (--wedgepattern is specified) then don't search for other wedges in the same RDR directory to detect available sitedrives")]
        public bool NoSearchForAdditionalWedges { get; set; }

        [Option(Default = null, HelpText = "Worker message queue name, required with --master")]
        public string WorkerQueueName { get; set; }

        [Option(Default = null, HelpText = "Orbital worker message queue name")]
        public string OrbitalWorkerQueueName { get; set; }

        [Option(Default = ProcessContextual.DEF_DEBOUNCE_SEC, HelpText = "Master waits at least this long after any RDR changed before firing a new contextual or orbital mesh message (unless an EOP is received), default if negative")]
        public int MasterDebounceSec { get; set; }

        [Option(Default = ProcessContextual.DEF_EOP_DEBOUNCE_SEC, HelpText = "Master waits at least this long after any EOP before firing a new contextual mesh message, default if negative")]
        public int MasterEOPDebounceSec { get; set; }

        [Option(Default = ProcessContextual.DEF_PLACESDB_CACHE_MAX_AGE_SEC, HelpText = "Cache PlacesDB results for up to this long, disabled if 0, default if negative")]
        public int MasterPlacesDBCacheMaxAgeSec { get; set; }

        [Option(Default = false, HelpText = "Don't re-initialize PlacesDB for each query when running as service. Default is to re-init, which means that credential locking will be more fine-grained.  Batch mode does not re-init.")]
        public bool MasterNoReinitPlacesDBPerQuery { get; set; }

        [Option(Default = false, HelpText = "Worker message queue(s) Landform owned")]
        public bool LandformOwnedWorkerQueue { get; set; }

        [Option(Default = ProcessContextual.DEF_MIN_PRIMARY_SITEDRIVE_WEDGES, HelpText = "Minimum number of wedges for primary site drive in a contextual mesh, non-positive for no limit, does not apply to highest-numbered sitedrive per sol per pass")]
        public int MinPrimarySiteDriveWedges { get; set; }

        [Option(Default = ProcessContextual.DEF_MAX_SITEDRIVES, HelpText = "Max number of site drives to include in contextual mesh, non-positive for no limit.  See MissionSpecific for finer grained limits on the number of products per instrument per sitedrive.")]
        public int MaxSiteDrives { get; set; }

        [Option(Default = ProcessContextual.DEF_MAX_SITEDRIVE_DISTANCE, HelpText = "Max distance in meters from origin of a site drive to origin of primary site drive to include in contextual mesh, non-positive for no limit")]
        public double MaxSiteDriveDistance { get; set; }

        [Option(Default = ProcessContextual.DEF_MAX_SOL_RANGE, HelpText = "Max difference between sol and primary sol to include in contextual mesh, negative to use default")]
        public int MaxSolRange { get; set; }

        [Option(Default = ProcessContextual.DEF_MAX_CONTEXTUAL_SITEDRIVES_PER_SOL, HelpText = "If positive, cull messages for older sitedrives to limit the total number of contextual messages per sol (existing messages are only included when combined with --coalesceexistingcontextualmessages)")]
        public int MaxContextualSiteDrivesPerSol { get; set; }

        [Option(Default = ProcessContextual.DEF_MAX_ORBITAL_SITEDRIVES_PER_SOL, HelpText = "If positive, cull messages for older sitedrives to limit the total number of orbital messages per sol (existing messages are only included when combined with --coalesceexistingorbitalmessages)")]
        public int MaxOrbitalSiteDrivesPerSol { get; set; }

        [Option(Default = null, HelpText = "Comma separated list of PLACES views for which to (re)build contextual tilesets when new solutions are added; null, empty, or \"none\" to disable triggering contextual on PLACES solution notifications")]
        public string ContextualPLACESSolutionViews { get; set; }

        [Option(Default = null, HelpText = "Comma separated list of PLACES views for which to (re)build orbital tilesets when new solutions are added; null, empty, or \"none\" to disable triggering orbital on PLACES solution notifications")]
        public string OrbitalPLACESSolutionViews { get; set; }

        [Option(Default = false, HelpText = "Don't search for the latest sol containing sitedrive on PLACES solution notification, if not already known from S3 ObjectCreated messages.")]
        public bool NoSearchForSolContainingSiteDriveOnPLACESNotification { get; set; }

        [Option(Default = false, HelpText = "Rebuild contextual mesh for previous end-of-drive RMC on PLACES solution notification")]
        public bool RebuildContextualAtPreviousEndOfDriveOnPLACESNotification { get; set; }

        [Option(Default = false, HelpText = "Only rebuild contextual mesh for previous end-of-drive RMC on PLACES solution notification")]
        public bool OnlyRebuildContextualAtPreviousEndOfDriveOnPLACESNotification { get; set; }

        [Option(Default = false, HelpText = "Disable triggering contextual tilesets on FDR S3 ObjectCreated; they could still be triggered by PLACES solution notifications")]
        public bool NoTriggerContextualOnRDRCreated { get; set; }

        [Option(Default = false, HelpText = "Disable triggering orbital tilesets on FDR S3 ObjectCreated; they could still be triggered by PLACES solution notifications")]
        public bool NoTriggerOrbitalOnFDRCreated { get; set; }

        [Option(Default = false, HelpText = "Allow rover observations for which no suitable rover mask is available or could be generated")]
        public bool AllowUnmaskedRoverObservations { get; set; }

        [Option(Default = null, HelpText = "Sol blacklist, 1-5,8,6-10")]
        public string SolBlacklist { get; set; }

        [Option(Default = false, HelpText = "Enable auto starting workers when running on EC2")]
        public bool AutoStartWorkers { get; set; }

        [Option(Default = ProcessContextual.DEF_WORKER_AUTOSTART_SEC, HelpText = "Period in seconds of worker auto start, non-positive to disable auto start")]
        public int WorkerAutoStartSec { get; set; }

        [Option(Default = null, HelpText = "Comma separated list of contextual worker EC2 instance ids (starting with \"i-\"), instance names, instance name wildcard patterns, or \"asg:<name>[:<size>]\" to use an auto scaling group (size defaults to 1)")]
        public string WorkerInstances { get; set; }

        [Option(Default = null, HelpText = "Comma separated list of orbital worker EC2 instance ids (starting with \"i-\"), instance names, or instance name wildcard patterns, or \"asg:<name>[:<size>]\" to use an auto scaling group (size defaults to 1)")]
        public string OrbitalWorkerInstances { get; set; }

        [Option(Default = null, HelpText = "Force tileset version.  Can be any integer or string not containing whitespace, slashes, or underscores.  Non-positive integers have the effect of un-setting the version.  If null or empty then the next available version number is automatically assigned.")]
        public string TilesetVersion { get; set; }

        [Option(Default = ProcessContextual.DEF_ZOMBIE_SEC, HelpText = "Consider existing PIDs zombie if not updated for longer than this (disabled if non-positive)")]
        public int ZombieSec { get; set; }
    }

    public class ProcessContextual : LandformService
    {
        public enum Phase
        { begin, fetch, ingest, align, geometry, leaves, blend, tileset, manifest, save, sky, combinedManifest, end };

        public const string FETCH_DIR = "fetched";

        new public const int DEF_MAX_HANDLER_SEC = 10 * 60 * 60; //10 hours
        public const int DEF_MASTER_MAX_HANDLER_SEC = 10 * 60; //10 minutes

        public const int DEF_WORKER_AUTOSTART_SEC = 5 * 60; //5 minutes

        public const int MASTER_LOOP_PERIOD_SEC = 10;

        public const int MAX_CONTEXTUAL_PASS_QUEUE_SIZE = 100;

        public const int MAX_VERSION = 100;
        public const int VERSION_INTERLOCK_SEC = 10;
        public const int DEF_ZOMBIE_SEC = 6 * 60 * 60; //6 hours

        //currently up to ~5min gaps in XYZ RDRs within one pass
        //but PlacesDB orbital solutions may not stabilize for up to 25 min after generation
        //of navcam orthomosaics
        public const int DEF_DEBOUNCE_SEC = 30 * 60; //30 minutes

        public const int DEF_EOP_DEBOUNCE_SEC = 20 * 60; //20 minutes

        public const int DEF_PLACESDB_CACHE_MAX_AGE_SEC = 1 * 24 * 60 * 60; //1 day

        public const int DEF_MIN_PRIMARY_SITEDRIVE_WEDGES = 4;
        public const int DEF_MAX_SITEDRIVES = 64;
        public const double DEF_MAX_SITEDRIVE_DISTANCE = 2 * BuildGeometry.DEF_SURFACE_EXTENT;
        public const int DEF_MAX_SOL_RANGE = 200;

        public const int DEF_ORBITAL_CHECK_EXISTING_SOL_RANGE = 30;

        //only make at most one contextual mesh per sol at highest numbered sitedrive
        public const int DEF_MAX_CONTEXTUAL_SITEDRIVES_PER_SOL = 1;

        //don't limit the number of orbital meshes per sol
        public const int DEF_MAX_ORBITAL_SITEDRIVES_PER_SOL = -1;

        public const string DEF_MAX_FETCH = "100G";
        public const string DEF_MAX_ORBITAL = "20G";

        public const string DEF_WEDGE_PATTERN = "*XYZ*.auto";
        public const string DEF_TEXTURE_PATTERN = "*mission*.auto";
        public const string DEF_FDR_PATTERN = "*FDR*.auto";
        public const string DEF_VCE_PATTERN = "*VCE|TRAV*";

        //EDRGen notifications
        //where INST is e.g. fcam, rcam, zcam, ncam, etc
        //empty S3 file: ids-pipeline/edrgen-status/INST/yyyy-MM-dd hh-mm-ss-INST.xml
        //sns/sqs message payload: "Edrgen done at yyyy-MM-dd hh-mm-ss"

        //EOF notifications (end-of-FDR)
        //https://jira.jpl.nasa.gov/browse/MSGDS-7447
        //empty S3 file: ids-pipeline/eof/yyyy-MM-dd-hh-mm-ss.eof
        //sns/sqs message payload: "Fdr done at yyyy-MM-dd hh-mm-ss"
        
        //EOX notifications (end-of-XYZ)
        //https://jira.jpl.nasa.gov/browse/MSGDS-7433
        //empty S3 file: ids-pipeline/eox/yyyy-MM-dd-hh-mm-ss.eox
        //sns/sqs message payload: "TODO at yyyy-MM-dd hh-mm-ss"

        //EOP notifications
        //empty S3 file: ids-pipeline/eop/yyyy-MM-dd-hh-mm-ss.eop
        //(but there can be variations, e.g. kgrimes-manual-yyyyMMdd.eop)
        //sns/sqs message payload: "EOP at yyyy-MM-dd hh-mm-ss"

        public const string DEF_EOP_FILE_PATTERN = "eop/*.eop"; 
        public const string DEF_EOF_FILE_PATTERN = "eof/*.eof"; 
        public const string DEF_EOX_FILE_PATTERN = "eox/*.eox"; //TODO

        //because we don't know details of the time format (e.g. timezone, 12/24h)
        //let's just conservatively look for "EOP"
        //and call the timestamp the time we received it
        //which is more in line with how we timestamp changed RDR URLs anyway
        //note these patterns will be applied case-insensitive
        public const string DEF_EOP_MESSAGE_PATTERN = "*EOP at*"; 
        public const string DEF_EOF_MESSAGE_PATTERN = "*Fdr done*"; 
        public const string DEF_EOX_MESSAGE_PATTERN = "*Xyz done*"; //TODO

        public const string DEF_PDS_EXTS = "IMG,VIC";

        public static bool orbitalCompareIgnoreSol;

        protected ProcessContextualOptions options;

        private Regex listRegex, wedgeRegex, textureRegex, fdrRegex, vceRegex;
        private Regex eopFileRegex, eofFileRegex, eoxFileRegex;
        private Regex eopMessageRegex, eofMessageRegex, eoxMessageRegex;

        private int debounceMS, eopDebounceMS;
        private int solRange, maxSDs;

        private SiteDrive minSiteDrive;

        private int[] solBlacklist;

        private volatile MessageQueue workerQueue, orbitalWorkerQueue;
        private List<string> workerInstances, orbitalWorkerInstances;
        private double lastWorkerAutoStartSec = -1;

        //list and wedge URLs for which we recently recived ObjectCreated (i.e. changed) messages
        //when MasterLoop() sees that none have changed for a given RDR directory in at least options.MasterDebounceSec
        //then that directory will be processed and its changed URLs cleared
        //synchronization is by locking this object itself
        private DictionaryOfChangedURLs changedContextualURLs = new DictionaryOfChangedURLs();
        private DictionaryOfChangedURLs changedOrbitalURLs = new DictionaryOfChangedURLs();

        private string[] contextualPlacesSolutionViews;
        private string[] orbitalPlacesSolutionViews;

        private class SolutionNotificationMessage : SNSMessageWrapper
        {
            public int Site { get; private set; } = -1;
            public int Drive { get; private set; } = -1;
            public string View { get; private set; }

            //see AssignSolAndRDRDir()
            public int sol = -1;
            public string rdrDir;

            private class SolutionNotification
            {
#pragma warning disable 0649
                public int site = -1;
                public int drive = -1;
                public string view;
#pragma warning restore 0649
            }

            public bool Parse(ILogger logger = null)
            {
                try
                {
                    var sn = JsonHelper.FromJson<SolutionNotification>(Message);
                    if (sn.site >= 0 && !string.IsNullOrEmpty(sn.view))
                    {
                        Site = sn.site;
                        Drive = sn.drive; //-1 for SITE
                        View = sn.view;
                        return true;
                    }
                }
                catch (Exception)
                {
                    //pass through
                }
                if (logger != null)
                {
                    logger.LogError("failed to parse SNS message as PLACES solution notification: " + Message);
                }
                return false;
            }

            public SiteDrive GetSiteDrive()
            {
                return new SiteDrive(Site, Math.Max(0, Drive));
            }

            public bool AssignedSolOrRDRDir()
            {
                return sol >= 0 || !string.IsNullOrEmpty(rdrDir);
            }

            public bool HasValidSolAndRDRDir()
            {
                return sol >= 0 && !string.IsNullOrEmpty(rdrDir) && rdrDir != "none";
            }

            public override string ToString()
            {
                string rmc = Drive >= 0 ? $"ROVER({Site},{Drive})" : $"SITE({Site})";
                string ret = $"PLACES solution notification for {rmc} in {View}";
                if (sol >= 0)
                {
                    ret += $", sol {sol}";
                }
                if (!string.IsNullOrEmpty(rdrDir))
                {
                    ret += $", FDR/RDR dir {rdrDir}";
                }
                return ret;
            }
        }

        //PLACES solution notifications that have been received by the main thread
        //and that are waiting to be processed by MasterLoop()
        //synchronized by locking the list itself
        private List<Stamped<SolutionNotificationMessage>> contextualPlacesSolutionNotifications =
            new List<Stamped<SolutionNotificationMessage>>();
        private List<Stamped<SolutionNotificationMessage>> orbitalPlacesSolutionNotifications =
            new List<Stamped<SolutionNotificationMessage>>();

        private class SolAndRDRDir
        {
            public int Sol { get; private set; } = -1;
            public string RDRDir { get; private set; }
            public SolAndRDRDir(int sol, string rdrDir)
            {
                this.Sol = sol;
                this.RDRDir = rdrDir;
            }
        }

        //sitedrive -> latest SOL and RDR dir from S3 ObjectCreated messages for recognized FDRs and RDRs, if any
        //"any" -> latest overall, if any
        //synchronized by locking the dictionary itself
        private Dictionary<string, SolAndRDRDir> latestSolAndRDRDir = new Dictionary<string, SolAndRDRDir>();

        private class ContextualPass
        {
            public readonly DictionaryOfChangedURLs urls;
            public readonly List<Stamped<SolutionNotificationMessage>> solutionMsgs;

            public ContextualPass(DictionaryOfChangedURLs urls, 
                                  List<Stamped<SolutionNotificationMessage>> solutionMsgs) {
                this.urls = urls;
                this.solutionMsgs = solutionMsgs;
            }
        }
        private Queue<ContextualPass> contextualPassQueue = new Queue<ContextualPass>();

        //if EOP messages are enabled this is the timestamp of the latest EOP message in UTC milliseconds
        //when one of these becomes positive MasterLoop() will process all pending changed URLs after the EOP debounce
        //a final eop is marked as a negative timestamp which will skip the debounce
        private long eopTimestamp;
        private long eofTimestamp; //orbital meshes can begin processing as soon as FDRs are done
        private long eoxTimestamp; //contextual meshes can begin processing as soon as XYZ,UVW,MXY,RAS are done

        private Dictionary<string, string> placesDBCache;
        private int placesDBCacheMaxAgeSec;
        private double placesDBCacheTime;

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
            public bool orbitalOnly;
            public long timestamp; //UTC milliseconds since epoch when message was created
            public int numFailedAttempts;
            public double extent = -1; //in meters, non-positive to use default
            public bool force; //rebuild even if tileset appears to already exist
#pragma warning restore 0649

            public override int GetHashCode()
            {
                int hash = HashCombiner.Combine(rdrDir.GetHashCode(), primarySiteDrive.GetHashCode(),
                                                orbitalOnly.GetHashCode(), extent.GetHashCode());
                if (!orbitalOnly || !ProcessContextual.orbitalCompareIgnoreSol)
                {
                    hash = HashCombiner.Combine(hash, primarySol);
                }
                return hash;
            }

            public override bool Equals(object obj)
            {
                if (!(obj is ContextualMeshMessage))
                {
                    return false;
                }
                return SameTileset(obj as ContextualMeshMessage);
            }

            public bool SameTileset(ContextualMeshMessage other, ILogger logger = null)
            {
                if (other == null)
                {
                    if (logger != null)
                    {
                        logger.LogInfo("messages differ: null");
                    }
                    return false;
                }

                //we now pre-normalize rdrDir before creating any ContextualMeshMessage
                //but keeping this here to cover corner cases where old messages may still be in queues
                if (ProcessContextual.NormalizeRDRDir(rdrDir) != ProcessContextual.NormalizeRDRDir(other.rdrDir))
                {
                    if (logger != null)
                    {
                        logger.LogInfo("messages differ: rdr dir \"{0}\" != \"{1}\"", rdrDir, other.rdrDir);
                    }
                    return false;
                }

                if (primarySiteDrive != other.primarySiteDrive)
                {
                    if (logger != null)
                    {
                        logger.LogInfo("messages differ: primary site drive \"{0}\" != \"{1}\"",
                                       primarySiteDrive, other.primarySiteDrive);
                    }
                    return false;
                }

                if (orbitalOnly != other.orbitalOnly)
                {
                    if (logger != null)
                    {
                        logger.LogInfo("messages differ: orbital only {0} != {1}", orbitalOnly, other.orbitalOnly);
                    }
                    return false;
                }

                if (extent != other.extent)
                {
                    if (logger != null)
                    {
                        logger.LogInfo("messages differ: extent {0} != {1}", extent, other.extent);
                    }
                    return false;
                }

                if ((!orbitalOnly || !ProcessContextual.orbitalCompareIgnoreSol) && primarySol != other.primarySol)
                {
                    if (logger != null)
                    {
                        logger.LogInfo("messages differ: primary sol {0} != {1}", primarySol, other.primarySol);
                    }
                    return false;
                }

                if (orbitalOnly)
                {
                    return true;
                }

                var mySols = ParseSols();
                var otherSols = other.ParseSols();
                if (!mySols.SetEquals(otherSols))
                {
                    if (logger != null)
                    {
                        logger.LogInfo("messages differ: sols {0} != {1}", ProcessContextual.MakeSolRanges(mySols),
                                       ProcessContextual.MakeSolRanges(otherSols));
                    }
                    return false;
                }

                var mySDs = ParseSiteDrives();
                var otherSDs = other.ParseSiteDrives();
                if (!mySDs.SetEquals(otherSDs))
                {
                    if (logger != null)
                    {
                        logger.LogInfo("messages differ: site drives {0} != {1}",
                                       string.Join(",", mySDs.OrderBy(sd => sd).ToArray()),
                                       string.Join(",", otherSDs.OrderBy(sd => sd).ToArray()));
                    }
                    return false;
                }

                //contextual meshes may differ even if they have the same sols and sitedrives
                //because if they are built at different times they may collect different sets of input RDRs
                //(timestamp is not actually the build time of the contextual mesh but it's the time that
                //the master requested a contextual mesh based on changes to available RDRs)
                if (timestamp != other.timestamp)
                {
                    if (logger != null)
                    {
                        logger.LogInfo("messages differ: timestamp {0} != {1}", UTCTime.MSSinceEpochToDate(timestamp),
                                       UTCTime.MSSinceEpochToDate(other.timestamp));
                    }
                    return false;
                }

                return true;
            }

            public HashSet<int> ParseSols(HashSet<int> ret = null)
            {
                ret = ret ?? new HashSet<int>();
                ret.Add(primarySol);
                if (!string.IsNullOrEmpty(sols))
                {
                    ret.UnionWith(IngestAlignmentInputs.ExpandSolSpecifier(sols));
                }
                return ret;
            }
            
            public HashSet<SiteDrive> ParseSiteDrives(HashSet<SiteDrive> ret = null)
            {
                ret = ret ?? new HashSet<SiteDrive>();
                ret.Add(new SiteDrive(primarySiteDrive));
                if (!string.IsNullOrEmpty(siteDrives))
                {
                    ret.UnionWith(SiteDrive.ParseList(siteDrives));
                }
                return ret;
            }

            public ContextualMeshMessage Clone()
            {
                return (ContextualMeshMessage)MemberwiseClone();
            }
        }

        private class EOPMessage : QueueMessage
        {
            public string eop;
        }

        private class EOFMessage : QueueMessage
        {
            public string eof;
        }

        private class EOXMessage : QueueMessage
        {
            public string eox;
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
            public int NumWedges = -1; //used only for information and sorting, negative if unknown
            public bool OrbitalOnly;
            public long Timestamp; //UTC milliseconds since epoch
            public double Extent;
        }
        
        private string DumpParameters(ContextualMeshParameters p, bool verbose = false)
        {
            var ret = string.Format("{0} mesh {1}_{2}", p.OrbitalOnly ? "orbital" : "contextual",
                                    SolToString(p.PrimarySol), p.PrimarySiteDrive);
            if (verbose)
            {
                ret += string.Format(" for {0}; sols {1}; sitedrives {2}",
                                     p.RDRDir, MakeSolRanges(p.Sols), string.Join(",", p.SiteDrives));
                if (p.NumWedges >= 0)
                {
                    ret += string.Format("; {0} wedges", p.NumWedges);
                }
                if (p.Timestamp > 0)
                {
                    ret += string.Format("; timestamp {0} UTC", UTCTime.MSSinceEpochToDate(p.Timestamp));
                }
                ret += string.Format("; extent {0}m", p.Extent);
            }
            return ret;
        }

        public ProcessContextual(ProcessContextualOptions options) : base(options)
        {
            this.options = options;
            defMaxHandlerSec = options.Master ? DEF_MASTER_MAX_HANDLER_SEC : DEF_MAX_HANDLER_SEC;
        }

        protected override void RunBatch()
        {
            RunPhase("build contextual tileset ", BuildContextualTileset);
        }

        protected override bool CanDeprioritizeRetries()
        {
            return !options.Master;
        }

        protected override QueueMessage MakeRecycledMessage(QueueMessage msg)
        {
            var cmm = msg as ContextualMeshMessage;
            if (cmm != null)
            {
                cmm.numFailedAttempts++;
                return cmm;
            }
            else
            {
                throw new NotImplementedException("cannot make recycled message, not a contextual mesh message");
            }
        }

        protected override double GetFirstSendMS(QueueMessage msg)
        {
            var cmm = msg as ContextualMeshMessage;
            if (cmm != null) {
                double ts = (double)(cmm.timestamp);
                if (ts > 0) {
                    return ts;
                }
            }
            return base.GetFirstSendMS(msg);
        }

        protected override int GetNumReceives(QueueMessage msg)
        {
            var cmm = msg as ContextualMeshMessage;
            return (cmm != null ? cmm.numFailedAttempts : 0) + msg.ApproxReceiveCount;
        }

        protected override string DescribeMessage(QueueMessage msg, bool verbose = false)
        {
            if (msg is EOPMessage)
            {
                return "EOP message: " + ((EOPMessage)msg).eop;
            }
            else if (msg is EOFMessage)
            {
                return "EOF message: " + ((EOFMessage)msg).eof;
            }
            else if (msg is EOXMessage)
            {
                return "EOX message: " + ((EOXMessage)msg).eox;
            }
            else if (msg is SolutionNotificationMessage)
            {
                return msg.ToString();
            }
            else if (msg is ContextualMeshMessage)
            {
                try
                {
                    var cmm = (msg as ContextualMeshMessage);
                    var parameters = MakeParameters(cmm);
                    if (parameters == null)
                    {
                        return "(invalid contextual mesh message)";
                    }
                    return DumpParameters(parameters, verbose);
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

        protected override void SendMessage()
        {
            string msg =
                options.SendMessage.IndexOf("://") >= 0 ? options.SendMessage : File.ReadAllText(options.SendMessage);
            if (options.Master &&
                ((eopMessageRegex != null && eopMessageRegex.IsMatch(msg)) ||
                 (eofMessageRegex != null && eofMessageRegex.IsMatch(msg)) ||
                 (eoxMessageRegex != null && eoxMessageRegex.IsMatch(msg)) ||
                 (TryParseSolutionNotification(msg) != null)))
            {
                pipeline.LogInfo("{0}sending EOP/EOF/EOX/PLACES message \"{1}\" to queue {2}",
                                 options.DryRun ? "dry " : "", msg, messageQueue.Name);
                if (!options.DryRun)
                {
                    messageQueue.Enqueue(msg);
                }
            }
            else
            {
                base.SendMessage();
            }
        }

        protected override QueueMessage ParseMessage(string msg)
        {
            if (options.Master)
            {
                return base.ParseMessage(msg);
            }
            var cmm = JsonHelper.FromJson<ContextualMeshMessage>(msg, autoTypes: false);
            cmm.timestamp = (long)UTCTime.NowMS();
            return cmm;
        }

        protected override bool AcceptMessage(QueueMessage msg, out string reason)
        {
            reason = null;
            string url = null;
            if (options.Master)
            {
                try
                {
                    if (msg is EOPMessage || msg is EOFMessage || msg is EOXMessage ||
                        msg is SolutionNotificationMessage)
                    {
                        return true;
                    }
                    url = GetUrlFromMessage(msg); 
                    if (string.IsNullOrEmpty(url))
                    {
                        reason = "no URL in message";
                        return false;
                    }
                    bool isList = listRegex != null && listRegex.IsMatch(url);
                    bool isWedge = wedgeRegex != null && wedgeRegex.IsMatch(url);
                    bool isTexture = textureRegex != null && textureRegex.IsMatch(url);
                    bool isFDR = fdrRegex != null && fdrRegex.IsMatch(url);
                    bool isVCE = vceRegex != null && vceRegex.IsMatch(url);
                    bool isEOP = eopFileRegex != null && eopFileRegex.IsMatch(url);
                    bool isEOF = eofFileRegex != null && eofFileRegex.IsMatch(url);
                    bool isEOX = eoxFileRegex != null && eoxFileRegex.IsMatch(url);
                    if ((!isList && !isWedge && !isTexture && !isFDR && !isEOP && !isEOF && !isEOX) || isVCE)
                    {
                        reason = "unhandled file type: " + url;
                        return false;
                    }
                    if (!AcceptBucketPath(url, allowInternal: isList || isEOP || isEOF || isEOX))
                    {
                        reason = "rejected bucket path: " + url;
                        return false;
                    }
                    Func<RoverProductId, string, string> filter = null; //? expression not allowed here until C# 9
                    if (isWedge)
                    {
                        filter = FilterWedge;
                    }
                    else if (isTexture)
                    {
                        filter = FilterTexture;
                    }
                    else if (isFDR)
                    {
                        filter = FilterFDR;
                    }
                    if (filter != null)
                    {
                        var idStr = StringHelper.GetLastUrlPathSegment(url, stripExtension: true);
                        var id = RoverProductId.Parse(idStr, mission, throwOnFail: false);
                        reason = filter(id, url);
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
                    reason = ex.GetType().Name + (!string.IsNullOrEmpty(ex.Message) ? (": " + ex.Message) : "") +
                        " url=\"" + url + "\"";
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

        private void AddChangedURL(DictionaryOfChangedURLs changedURLs, string rdrDir, SiteDrive sd, string url,
                                   long now)
        {
            lock (changedURLs)
            {
                if (!changedURLs.ContainsKey(rdrDir))
                {
                    changedURLs[rdrDir] = new Dictionary<SiteDrive, Dictionary<string, long>>();
                }
                if (!changedURLs[rdrDir].ContainsKey(sd))
                {
                    changedURLs[rdrDir][sd] = new Dictionary<string, long>();
                }
                changedURLs[rdrDir][sd][url] = now;
            }
        }

        private int FinalEOP(string txt)
        {
            txt = StringHelper.GetLastUrlPathSegment(txt, stripExtension: true).Trim();
            return txt.EndsWith("final", StringComparison.OrdinalIgnoreCase) ? -1 : 1;
        }

        protected override bool HandleMessage(QueueMessage msg)
        {
            if (options.Master)
            {
                if (msg is EOPMessage)
                {
                    string txt = ((EOPMessage)msg).eop.Trim();
                    int sign = FinalEOP(txt);
                    Interlocked.Exchange(ref eopTimestamp, sign * (long)UTCTime.NowMS());
                    pipeline.LogInfo("received EOP message \"{0}\"{1}", txt, sign < 0 ? " (final)" : "");
                    return true; //successfully processed, remove message from queue
                }
                if (msg is EOFMessage)
                {
                    string txt = ((EOFMessage)msg).eof.Trim();
                    int sign = FinalEOP(txt);
                    Interlocked.Exchange(ref eofTimestamp, sign * (long)UTCTime.NowMS());
                    pipeline.LogInfo("received EOF message \"{0}\"{1}", txt, sign < 0 ? " (final)" : "");
                    return true; //successfully processed, remove message from queue
                }
                if (msg is EOXMessage)
                {
                    string txt = ((EOXMessage)msg).eox.Trim();
                    int sign = FinalEOP(txt);
                    Interlocked.Exchange(ref eoxTimestamp, sign * (long)UTCTime.NowMS());
                    pipeline.LogInfo("received EOX message \"{0}\"{1}", txt, sign < 0 ? " (final)" : "");
                    return true; //successfully processed, remove message from queue
                }
                if (msg is SolutionNotificationMessage)
                {
                    var snm = msg as SolutionNotificationMessage;
                    if (snm.Site < minSiteDrive.Site)
                    {
                        pipeline.LogWarn("ignoring {0}, invalid RMC", snm);
                    }
                    else
                    {
                        bool forContextual = false, forOrbital = false;
                        if (!options.NoSurface && contextualPlacesSolutionViews != null &&
                            contextualPlacesSolutionViews.Any(v => v.Equals(snm.View)))
                        {
                            forContextual = true;
                            lock (contextualPlacesSolutionNotifications)
                            {
                                contextualPlacesSolutionNotifications
                                    .Add(new Stamped<SolutionNotificationMessage>(snm));
                            }
                        }
                        if (!options.NoOrbital && orbitalPlacesSolutionViews != null &&
                            orbitalPlacesSolutionViews.Any(v => v.Equals(snm.View)))
                        {
                            forOrbital = true;
                            lock (orbitalPlacesSolutionNotifications)
                            {
                                orbitalPlacesSolutionNotifications
                                    .Add(new Stamped<SolutionNotificationMessage>(snm));
                            }
                        }
                        pipeline.LogInfo("{0}registering {1} for {2} triggering",
                                         (forContextual || forOrbital) ? "" : "not ", snm,
                                         (forContextual && forOrbital) ? "both contextual and orbital" :
                                         forContextual ? "only contextual" : forOrbital ? "only orbital" :
                                         "contextual or orbital");
                    }
                    return true; //successfully processed, remove message from queue
                }
                string url = StringHelper.NormalizeUrl(GetUrlFromMessage(msg)); 
                if (!FileExists(url))
                {
                    pipeline.LogWarn("file {0} not found", url);
                    return true; //drop message, maybe file was deleted or renamed
                }
                if (eopFileRegex != null && eopFileRegex.IsMatch(url))
                {
                    int sign = FinalEOP(url);
                    Interlocked.Exchange(ref eopTimestamp, sign * (long)UTCTime.NowMS());
                    pipeline.LogInfo("processed EOP file {0}{1}", url, sign < 0 ? " (final)" : "");
                    return true; //successfully processed, remove message from queue
                }
                if (eofFileRegex != null && eofFileRegex.IsMatch(url))
                {
                    int sign = FinalEOP(url);
                    Interlocked.Exchange(ref eofTimestamp, sign * (long)UTCTime.NowMS());
                    pipeline.LogInfo("processed EOF file {0}{1}", url, sign < 0 ? " (final)" : "");
                    return true; //successfully processed, remove message from queue
                }
                if (eoxFileRegex != null && eoxFileRegex.IsMatch(url))
                {
                    int sign = FinalEOP(url);
                    Interlocked.Exchange(ref eoxTimestamp, sign * (long)UTCTime.NowMS());
                    pipeline.LogInfo("processed EOX file {0}{1}", url, sign < 0 ? " (final)" : "");
                    return true; //successfully processed, remove message from queue
                }
                var rdrDir = SiteDriveList.GetRDRDir(url);
                if (rdrDir == null && listRegex != null && listRegex.IsMatch(url))
                {
                    //this will happen e.g. for s3://BUCKET/ids-pipeline/xyz_SSSDDDD.lis
                    //this is maybe a bit wasteful but the download should be cached
                    //and the cost of parsing the list file multiple times shouldn't be a big deal
                    var sdList = new SiteDriveList(mission, pipeline, FilterWedge, FilterTexture);
                    LoadList(sdList, url);
                    rdrDir = sdList.RDRDir; //might still be null if all wedges were filtered
                }
                if (rdrDir == null)
                {
                    pipeline.LogWarn("failed to parse RDR dir from URL {0}", url);
                    return true; //drop message
                }
                rdrDir = NormalizeRDRDir(rdrDir);
                var idStr = StringHelper.GetLastUrlPathSegment(url, stripExtension: true);
                var id = RoverProductId.Parse(idStr, mission, throwOnFail: false);
                var sd = GetSiteDrive(url, id);
                if (!sd.HasValue)
                {
                    pipeline.LogWarn("failed to parse site drive from URL {0}", url);
                    return true; //drop message
                }
                bool isFDR = fdrRegex != null && fdrRegex.IsMatch(url);
                bool isRDR = wedgeRegex != null && wedgeRegex.IsMatch(url) ||
                    textureRegex != null && textureRegex.IsMatch(url);
                if (isFDR || isRDR)
                {
                    int sol = SiteDriveList.GetSol(url, id);
                    if (sol >= 0)
                    {
                        string sdStr = sd.ToString();
                        lock (latestSolAndRDRDir)
                        {
                            foreach (string key in new string[] { sdStr, "any" })
                            {
                                if (!latestSolAndRDRDir.ContainsKey(key) || latestSolAndRDRDir[key].Sol < sol)
                                {
                                    pipeline.LogInfo("setting latest RDR sol to {0}, RDR dir {1} for sitedrive \"{2}\"",
                                                     sol, rdrDir, key);
                                    latestSolAndRDRDir[key] = new SolAndRDRDir(sol, rdrDir);
                                }
                            }
                        }
                    }
                    else
                    {
                        pipeline.LogWarn("failed to parse sol from {0} ({1})", url, id);
                    }
                }
                string what = "", reason = null;
                long now = (long)UTCTime.NowMS();
                if (isFDR)
                {
                    what = "orbital ";
                    Interlocked.Exchange(ref eofTimestamp, 0);
                    if (options.NoOrbital)
                    {
                        reason = "orbital data disabled";
                    }
                    else if (options.NoTriggerOrbitalOnFDRCreated)
                    {
                        reason = "orbital FDR trigger disabled";
                    }
                    else if (!mission.UseForOrbitalTriggering(id))
                    {
                        reason = "product ID disabled by mission for orbital trigger";
                    }
                    else
                    {
                        AddChangedURL(changedOrbitalURLs, rdrDir, sd.Value, url, now);
                    }
                }
                else if (isRDR)
                {
                    what = "contextual ";
                    Interlocked.Exchange(ref eoxTimestamp, 0);
                    Interlocked.Exchange(ref eopTimestamp, 0);
                    if (options.NoSurface)
                    {
                        reason = "surface data disabled";
                    }
                    else if (options.NoTriggerContextualOnRDRCreated)
                    {
                        reason = "contextual RDR trigger disabled";
                    }
                    else if (!mission.UseForContextualTriggering(id))
                    {
                        reason = "product ID disabled by mission for contextual trigger";
                    }
                    else
                    {
                        AddChangedURL(changedContextualURLs, rdrDir, sd.Value, url, now);
                    }
                }
                else
                {
                    reason = "not a recognized FDR or RDR wedge or texture";
                }
                if (string.IsNullOrEmpty(reason))
                {
                    pipeline.LogInfo("registered changed URL for {0}triggering on EOP or timeout at {1}: {2}",
                                     what, ToLocalTime(now), url);
                }
                else
                {
                    pipeline.LogInfo("not registering changed URL for {0}triggering on EOP or timeout at {1}, {2}: {3}",
                                     what, ToLocalTime(now), reason, url);
                }
                return true; //successfully processed, remove message from queue
            }
            else if (msg is ContextualMeshMessage)
            {
                var parameters = MakeParameters(msg as ContextualMeshMessage);
                if (parameters != null)
                {
                    ResetWatchdogStats();
                    BuildContextualTileset(parameters); //throws exception on error or if killed
                    string stats = GetWatchdogStats();
                    if (!string.IsNullOrEmpty(stats))
                    {
                        pipeline.LogInfo("memory watchdog: {0}", stats);
                    }
                }
                return true; //message ignored or successfully processed, remove from queue
            }
            else
            {
                pipeline.LogWarn("unknown message type {0}, dropping message", msg.GetType().Name);
                return true; //drop message
            }
        }

        protected override QueueMessage AlternateMessageHandler(string txt)
        {
            if (!options.Master)
            {
                return null;
            }
            if (eopMessageRegex != null && eopMessageRegex.IsMatch(txt))
            {
                return new EOPMessage() { eop = txt };
            }
            if (eofMessageRegex != null && eofMessageRegex.IsMatch(txt))
            {
                return new EOFMessage() { eof = txt };
            }
            if (eoxMessageRegex != null && eoxMessageRegex.IsMatch(txt))
            {
                return new EOXMessage() { eox = txt };
            }
            var snm = TryParseSolutionNotification(txt);
            if (snm != null)
            {
                return snm;
            }
            return base.AlternateMessageHandler(txt);
        }

        private string ParseRDRExtension(string filenamePattern, string what)
        {
            if (filenamePattern.EndsWith(".auto", StringComparison.OrdinalIgnoreCase))
            {
                return filenamePattern.Substring(0, filenamePattern.Length - 5) +
                    (mission.PreferIMGToVIC() ? ".IMG" : ".VIC");
            }
            else if (filenamePattern.EndsWith(".VIC", StringComparison.OrdinalIgnoreCase) ||
                     filenamePattern.EndsWith(".IMG", StringComparison.OrdinalIgnoreCase))
            {
                return filenamePattern;
            }
            throw new Exception("invalid extension for " + what + "=\"" + filenamePattern +
                                "\", must be .IMG, .VIC, or .auto");
        }

        private Regex MakeURLRegex(string filenamePattern, string exts = null)
        {
            string fp = exts == null ? filenamePattern : StringHelper.StripUrlExtension(filenamePattern);
            string fr = StringHelper.WildcardToRegularExpressionString(fp, fullMatch: false, matchSlashes: false,
                                                                       allowAlternation: true);
            string er = exts != null ? ("[.](" + exts.ToUpper().Replace(",","|") + ")") : "";
            return new Regex("^.*/" + fr + er + "$", RegexOptions.IgnoreCase);
        }

        private List<string> FilterURLs(List<string> urls, string preferredExts)
        {
            if (string.IsNullOrEmpty(preferredExts))
            {
                return urls;
            }
            var uniq = new HashSet<string>(urls.Count);
            var ret = new List<string>(urls.Count);
            foreach (var ext in StringHelper.ParseList(preferredExts))
            {
                foreach (string url in urls.Where(url => url.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                {
                    string pfx = StringHelper.StripUrlExtension(url);
                    if (!uniq.Contains(pfx))
                    {
                        uniq.Add(pfx);
                        ret.Add(url);
                    }
                }
            }
            return ret;
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
                if (string.IsNullOrEmpty(options.RDRDir) || string.IsNullOrEmpty(options.Sols))
                {
                    throw new Exception("--rdrdir and --sols required without --service or --master");
                }
            }
            else if (!string.IsNullOrEmpty(options.Sols) || !string.IsNullOrEmpty(options.SiteDrives))
            {
                throw new Exception("cannot combine --sols or --sitedrives with --service or --master");
            }

            if (!string.IsNullOrEmpty(options.WedgePattern) &&
                !string.Equals(options.WedgePattern, "none", StringComparison.OrdinalIgnoreCase))
            {
                options.WedgePattern = ParseRDRExtension(options.WedgePattern, "--wedgepattern");
                wedgeRegex = MakeURLRegex(options.WedgePattern);
                pipeline.LogInfo("wedge regex: " + wedgeRegex);
            }
            
            if (!string.IsNullOrEmpty(options.TexturePattern) &&
                !string.Equals(options.TexturePattern, "none", StringComparison.OrdinalIgnoreCase))
            {
                options.TexturePattern = options.TexturePattern.Replace("mission", mission.GetImageProductType());
                options.TexturePattern = ParseRDRExtension(options.TexturePattern, "--texturepattern");
                textureRegex = MakeURLRegex(options.TexturePattern);
                pipeline.LogInfo("texture regex: " + textureRegex);
            }
            
            if (!string.IsNullOrEmpty(options.FDRPattern) &&
                !string.Equals(options.FDRPattern, "none", StringComparison.OrdinalIgnoreCase))
            {
                fdrRegex = MakeURLRegex(ParseRDRExtension(options.FDRPattern, "--fdrpattern"));
                pipeline.LogInfo("FDR regex: " + fdrRegex);
            }
            
            if (!string.IsNullOrEmpty(options.VCEPattern) &&
                !string.Equals(options.VCEPattern, "none", StringComparison.OrdinalIgnoreCase))
            {
                vceRegex = MakeURLRegex(options.VCEPattern);
                pipeline.LogInfo("VCE regex: " + vceRegex);
            }

            if (options.Master)
            {
                if (!string.IsNullOrEmpty(options.ListPattern) &&
                    !string.Equals(options.ListPattern, "none", StringComparison.OrdinalIgnoreCase))
                {
                    listRegex = MakeURLRegex(options.ListPattern);
                    pipeline.LogInfo("list regex: " + listRegex);
                }

                if (!string.IsNullOrEmpty(options.EOPFilePattern) &&
                    !string.Equals(options.EOPFilePattern, "none", StringComparison.OrdinalIgnoreCase))
                {
                    eopFileRegex = MakeURLRegex(options.EOPFilePattern);
                    pipeline.LogInfo("EOP file regex: " + eopFileRegex);
                }

                if (!string.IsNullOrEmpty(options.EOPMessagePattern) &&
                    !string.Equals(options.EOPMessagePattern, "none", StringComparison.OrdinalIgnoreCase))
                {
                    eopMessageRegex =
                        StringHelper.WildcardToRegularExpression(options.EOPMessagePattern, fullMatch: true,
                                                                 allowAlternation: true, opts: RegexOptions.IgnoreCase);
                    pipeline.LogInfo("EOP message regex: " + eopMessageRegex);
                }

                if (!string.IsNullOrEmpty(options.EOFFilePattern) &&
                    !string.Equals(options.EOFFilePattern, "none", StringComparison.OrdinalIgnoreCase))
                {
                    eofFileRegex = MakeURLRegex(options.EOFFilePattern);
                    pipeline.LogInfo("EOF file regex: " + eofFileRegex);
                }

                if (!string.IsNullOrEmpty(options.EOFMessagePattern) &&
                    !string.Equals(options.EOFMessagePattern, "none", StringComparison.OrdinalIgnoreCase))
                {
                    eofMessageRegex =
                        StringHelper.WildcardToRegularExpression(options.EOFMessagePattern, fullMatch: true,
                                                                 allowAlternation: true, opts: RegexOptions.IgnoreCase);
                    pipeline.LogInfo("EOF message regex: " + eofMessageRegex);
                }

                if (!string.IsNullOrEmpty(options.EOXFilePattern) &&
                    !string.Equals(options.EOXFilePattern, "none", StringComparison.OrdinalIgnoreCase))
                {
                    eoxFileRegex = MakeURLRegex(options.EOXFilePattern);
                    pipeline.LogInfo("EOX file regex: " + eoxFileRegex);
                }

                if (!string.IsNullOrEmpty(options.EOXMessagePattern) &&
                    !string.Equals(options.EOXMessagePattern, "none", StringComparison.OrdinalIgnoreCase))
                {
                    eoxMessageRegex =
                        StringHelper.WildcardToRegularExpression(options.EOXMessagePattern, fullMatch: true,
                                                                 allowAlternation: true, opts: RegexOptions.IgnoreCase);
                    pipeline.LogInfo("EOX message regex: " + eoxMessageRegex);
                }

                if (!serviceUtilMode || options.DeleteQueues)
                {
                    if (string.IsNullOrEmpty(options.WorkerQueueName) && !options.DeleteQueues)
                    {
                        throw new Exception("--workerqueuename required with --master");
                    }
                    workerQueue = GetWorkerMessageQueue();
                    if (workerQueue != null)
                    {
                        pipeline.LogInfo("worker queue: {0}", workerQueue.Name);
                    }
                    if (!string.IsNullOrEmpty(options.OrbitalWorkerQueueName) && !options.DeleteQueues &&
                        !options.NoOrbital)
                    {
                        orbitalWorkerQueue = GetOrbitalWorkerMessageQueue();
                        if (orbitalWorkerQueue != null)
                        {
                            pipeline.LogInfo("orbital worker queue: {0}", orbitalWorkerQueue.Name);
                        }
                    }
                }

                if (!serviceUtilMode)
                {
                    if (options.AutoStartWorkers && options.WorkerAutoStartSec > 0)
                    {
                        pipeline.LogInfo("worker auto start enabled, period {0}",
                                         Fmt.HMS(options.WorkerAutoStartSec * 1e3));
                        workerInstances = GetInstances(options.WorkerInstances);
                        orbitalWorkerInstances = GetInstances(options.OrbitalWorkerInstances, "orbital");
                    }
                    else
                    {
                        pipeline.LogInfo("worker auto start disabled");
                    }
                }
            }

            debounceMS = 1000 * (options.MasterDebounceSec >= 0 ? options.MasterDebounceSec : DEF_DEBOUNCE_SEC);
            pipeline.LogInfo("RDR debounce time {0}s", debounceMS / 1000);

            eopDebounceMS = 1000 * (options.MasterEOPDebounceSec >= 0 ?
                                    options.MasterEOPDebounceSec : DEF_EOP_DEBOUNCE_SEC);
            pipeline.LogInfo("EOP debounce time {0}s", eopDebounceMS / 1000);

            placesDBCacheMaxAgeSec = (options.MasterPlacesDBCacheMaxAgeSec >= 0 ?
                                      options.MasterPlacesDBCacheMaxAgeSec : DEF_PLACESDB_CACHE_MAX_AGE_SEC);
            pipeline.LogInfo("PlacesDB cache max age: {0}", Fmt.HMS(placesDBCacheMaxAgeSec * 1e3));

            solRange = options.MaxSolRange >= 0 ? options.MaxSolRange : DEF_MAX_SOL_RANGE;
            maxSDs = options.MaxSiteDrives > 0 ? options.MaxSiteDrives : int.MaxValue;
            pipeline.LogInfo("default extent {0}, surface extent {1}", options.Extent, options.SurfaceExtent);
            pipeline.LogInfo("max sol range {0}, max sitedrives {1}", solRange, maxSDs);
            pipeline.LogInfo("min wedges for primary sitedrive {0}", options.MinPrimarySiteDriveWedges);
            pipeline.LogInfo("max contextual sitedrives per sol {0}", options.MaxContextualSiteDrivesPerSol > 0 ?
                             options.MaxContextualSiteDrivesPerSol.ToString() : "unlimited");
            pipeline.LogInfo("max orbital sitedrives per sol {0}", options.MaxOrbitalSiteDrivesPerSol > 0 ?
                             options.MaxOrbitalSiteDrivesPerSol.ToString() : "unlimited");

            pipeline.LogInfo("max wedges {0}, max textures {1}",
                             mission.GetContextualMeshMaxWedges(), mission.GetContextualMeshMaxTextures());
            pipeline.LogInfo("max navcam wedges {0} per sitedrive, max navcam textures {1} per sitedrive",
                             mission.GetContextualMeshMaxNavcamWedgesPerSiteDrive(),
                             mission.GetContextualMeshMaxNavcamTexturesPerSiteDrive());
            pipeline.LogInfo("max mastcam wedges {0} per sitedrive, max mastcam textures {1} per sitedrive",
                             mission.GetContextualMeshMaxMastcamWedgesPerSiteDrive(),
                             mission.GetContextualMeshMaxMastcamTexturesPerSiteDrive());
            pipeline.LogInfo("when limits are exceeded prefer {0} products",
                             mission.GetContextualMeshPreferOlderProducts() ? "older" : "newer");

            solBlacklist = IngestAlignmentInputs.ExpandSolSpecifier(options.SolBlacklist);
            if (solBlacklist.Length > 0)
            {
                pipeline.LogInfo("{0} blacklisted sols: {1}", solBlacklist.Length, MakeSolRanges(solBlacklist));
            }

            orbitalCompareIgnoreSol = !options.NoOrbitalCompareIgnoreSol;
            if (orbitalCompareIgnoreSol)
            {
                pipeline.LogInfo("ignoring sol when comparing orbital tilesets");
            }

            minSiteDrive = mission.GetMinSiteDrive();

            var fdrSearchDirs = mission.GetFDRSearchDirs();
            if (fdrSearchDirs == null || fdrSearchDirs.Count == 0)
            {
                pipeline.LogWarn("disabling PLACES solution notifications, no FDR search dirds");
                options.ContextualPLACESSolutionViews = "none";
                options.OrbitalPLACESSolutionViews = "none";
            }
            if (!string.IsNullOrEmpty(options.ContextualPLACESSolutionViews) &&
                !string.Equals(options.ContextualPLACESSolutionViews, "none", StringComparison.OrdinalIgnoreCase))
            {
                pipeline.LogInfo("(re)building contextual meshes for new PLACES solutions in views: " +
                                 options.ContextualPLACESSolutionViews);
                contextualPlacesSolutionViews = StringHelper.ParseList(options.ContextualPLACESSolutionViews);
            }
            if (!string.IsNullOrEmpty(options.OrbitalPLACESSolutionViews) &&
                !string.Equals(options.OrbitalPLACESSolutionViews, "none", StringComparison.OrdinalIgnoreCase))
            {
                pipeline.LogInfo("(re)building orbital meshes for new PLACES solutions in views: " +
                                 options.OrbitalPLACESSolutionViews);
                orbitalPlacesSolutionViews = StringHelper.ParseList(options.OrbitalPLACESSolutionViews);
            }

            options.RebuildContextualAtPreviousEndOfDriveOnPLACESNotification |=
                options.OnlyRebuildContextualAtPreviousEndOfDriveOnPLACESNotification;

            return true;
        }

        //uses EC2, called only by ParseArguments()
        protected List<string> GetInstances(string opt, string what = "")
        {
            var patterns = StringHelper.ParseList(opt);
            what = (!string.IsNullOrEmpty(what) ? (what + " ") : "") + "worker instance IDs";

            var ret = new List<string>();

            if (patterns.Any(pattern => pattern.StartsWith("asg:")))
            {
                if (patterns.Length > 1)
                {
                    throw new Exception(string.Format("invalid option for {0}, " +
                                                      "\"asg:<name>[:<size>]\" must be alone: {1}",
                                                      what, String.Join(", ", patterns)));
                }
                ret.Add(patterns[0]);
            }
            else
            {
                foreach (string pattern in patterns)
                {
                    if (pattern.StartsWith("i-"))
                    {
                        ret.Add(pattern);
                    }
                    else
                    {
                        try
                        {
                            var ids = computeHelper.InstanceNamePatternToIDs(pattern);
                            if (ids.Count > 0)
                            {
                                ret.AddRange(ids);
                            }
                            else
                            {
                                pipeline.LogWarn("failed to find {0} for name pattern \"{1}\"", what, pattern);
                            }
                        }
                        catch (Exception ex)
                        {
                            pipeline.LogException(ex, "failed to create EC2 client");
                        }
                    }
                }
            }

            if (ret.Count > 0)
            {
                pipeline.LogInfo("{0} {1}: {2}", ret.Count, what, String.Join(", ", ret));
            }
            else
            {
                pipeline.LogWarn("no {0}", what);
            }

            return ret.Count > 0 ? ret : null;
        }

        protected override bool RequiresCredentialRefresh()
        {
            return true; //CSSO credentials are needed for PlacesDB
        }

        protected override void RefreshCredentials()
        {
            base.RefreshCredentials();

            if (workerQueue != null && options.NoUseDefaultAWSProfileForSQSClient)
            {
                workerQueue.Dispose();
                workerQueue = GetWorkerMessageQueue();
            }

            if (orbitalWorkerQueue != null && options.NoUseDefaultAWSProfileForSQSClient)
            {
                orbitalWorkerQueue.Dispose();
                orbitalWorkerQueue = GetOrbitalWorkerMessageQueue();
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

        protected override string GetSubcommandLogFile()
        {
            string lf = Logging.GetLogFile();
            string bn = Path.GetFileNameWithoutExtension(lf);
            string ext = Path.GetExtension(lf);

            if (bn.Contains("process-contextual"))
            {
                bn = bn.Replace("process-contextual", "process-contextual-subcommands");
            }
            else if (bn.Contains("contextual-service"))
            {
                bn = bn.Replace("contextual-service", "contextual-subcommands");
            }
            else if (bn.Contains("contextual-master"))
            {
                bn = bn.Replace("contextual-master", "contextual-master-subcommands");
            }
            else
            {
                bn = bn + "-subcommands";
            }

            return bn + ext;
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
            DeleteQueue(orbitalWorkerQueue, "orbital worker"); //null ok
        }

        protected override void RunService()
        {
            if (options.Master)
            {
                Task.Run(() => MasterLoop());
                Task.Run(() => ContextualPassLoop());
            }
            base.RunService();
        }

        protected override void DumpExtraStats()
        {
            if (!serviceMode)
            {
                base.DumpExtraStats();
            }
        }

        protected override bool UseMessageStopwatch()
        {
            return !options.Master;
        }

        protected override bool SuppressRejections()
        {
            return options.Master;
        }

        public static string MakeSolRanges(HashSet<int> sols)
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

        public static string MakeSolRanges(int[] sols)
        {
            return MakeSolRanges(new HashSet<int>(sols));
        }

        private void RemoveBlacklistedSols(HashSet<int> sols, int primarySol)
        {
            HashSet<int> blacklisted = null;
            if (solBlacklist.Length > 0)
            {
                blacklisted = new HashSet<int>();
                blacklisted.UnionWith(sols);
                blacklisted.IntersectWith(solBlacklist);
                blacklisted.Remove(primarySol);
                if (blacklisted.Count > 0)
                {
                    pipeline.LogInfo("removing {0} blacklisted sols: {1}",
                                     blacklisted.Count, MakeSolRanges(blacklisted));
                    sols.ExceptWith(blacklisted);
                }
            }
            var skipped = new HashSet<int>(sols.Where(sol => Math.Abs(sol - primarySol) > solRange));
            if (skipped.Count > 0)
            {
                pipeline.LogWarn("removed {0} sols out of range {1} from primary sol {2}",
                                 skipped.Count, solRange, primarySol);
                sols.ExceptWith(skipped);
            }
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
            msg.ParseSols(ret.Sols);
            
            ret.PrimarySiteDrive = new SiteDrive(msg.primarySiteDrive);
            msg.ParseSiteDrives(ret.SiteDrives);

            ret.NumWedges = msg.numWedges;
            ret.Timestamp = msg.timestamp;

            ret.OrbitalOnly = msg.orbitalOnly;

            ret.Extent = msg.extent;

            return ret;
        }

        private ContextualMeshParameters MakeBatchParameters()
        {
            return MakeContextualMeshParameters(options.RDRDir, options.Sols, options.SiteDrives, options.NoSurface,
                                                options.Extent);
        }
                
        private ContextualMeshParameters MakeContextualMeshParameters(string rdrDir, string sols, string siteDrives,
                                                                      bool orbitalOnly, double extent,
                                                                      Action<ContextualMeshMessage> msgCallback = null)
        {

            int sep = sols.IndexOfAny(new char[] { ',', '-' });
            sep = sep < 0 ? sols.Length : sep;
            int primarySol = int.Parse(sols.Substring(0, sep));
            var allSols = new HashSet<int>(IngestAlignmentInputs.ExpandSolSpecifier(sols));
            RemoveBlacklistedSols(allSols, primarySol); //also enforces solRange
            if (allSols.Count == 0)
            {
                pipeline.LogInfo("no sols");
                return null;
            }
            string solRanges = MakeSolRanges(allSols);
            pipeline.LogInfo("primary sol {0}, all sols {1}", primarySol, solRanges);

            if (string.IsNullOrEmpty(siteDrives) || siteDrives.ToLower().Contains("auto"))
            {
                var allSDs = FindAllSiteDrives(rdrDir, primarySol, allSols);

                SiteDrive? primarySD = null;
                var givenSDs = StringHelper.ParseList(siteDrives);
                if (givenSDs.Length > 0 && givenSDs[0].ToLower() != "auto")
                {
                    primarySD = new SiteDrive(givenSDs[0]);
                    if (!allSDs.ContainsKey(primarySD.Value))
                    {
                        pipeline.LogError("manually specified primary sitedrive {0} not found in sols {1}",
                                          primarySD.Value, solRanges);
                        return null;
                    }
                }
                else if (allSDs.Count > 0)
                {
                    var primarySDCandidates = allSDs.Keys.Where(sd => allSDs[sd].Sols.Contains(primarySol)).ToList();
                    if (primarySDCandidates.Count == 0)
                    {
                        pipeline.LogError("no sitedrives in sol {0}", primarySol);
                        return null;
                    }
                    //minWedges is not applied to the highest numbered sitedrive in a sol
                    //so the auto primary sitedrive for a sol is always the highest numbered one
                    //int minWedges = options.MinPrimarySiteDriveWedges;
                    //if (minWedges > 0)
                    //{
                    //    primarySDCandidates = primarySDCandidates
                    //        .Where(sd => allSDs[sd].NumWedges >= minWedges)
                    //        .ToList();
                    //    if (primarySDCandidates.Count == 0)
                    //    {
                    //        pipeline.LogError("no sitedrives with at least {0} wedges in sol {1}",
                    //                          minWedges, primarySol);
                    //        return null;
                    //    }
                    //}
                    primarySD = primarySDCandidates.OrderByDescending(sd => sd).First();
                }

                if (givenSDs.Length > 0)
                {
                    var sds = new HashSet<SiteDrive>();
                    sds.UnionWith(allSDs.Keys);
                    sds.UnionWith(givenSDs.Where(sd => sd.ToLower() != "auto").Select(sd => new SiteDrive(sd)));
                    foreach (var sd in sds)
                    {
                        if (!allSDs.ContainsKey(sd))
                        {
                            pipeline.LogError("manually specified sitedrive {0} not found in sols {1}", sd, solRanges);
                            return null;
                        }
                    }
                }
                    
                if (allSDs.Count == 0 || !primarySD.HasValue)
                {
                    pipeline.LogError("no non-empty sitedrives for sols {0}", solRanges);
                    return null;
                }

                var changedSDsBySol = new Dictionary<int, List<SiteDrive>>();
                changedSDsBySol[primarySol] = new List<SiteDrive>();
                changedSDsBySol[primarySol].Add(primarySD.Value);

                var placesDB = !orbitalOnly ? InitPlacesDB() : null;
                var msg = orbitalOnly ? MakeOrbitalMeshMessage(allSDs[primarySD.Value]) :
                    MakeContextualMeshMessage(primarySD.Value, changedSDsBySol, allSDs, placesDB);

                if (msg == null)
                {
                    pipeline.LogInfo("available sitedrives {0} for sols {1}, primary sol {2}, primary sitedrive {3} " +
                                     "did not meet contextual mesh criteria",
                                     string.Join(",", allSDs.Keys.Select(sd => sd.ToString())),
                                     sols, primarySol, primarySD);
                    return null;
                }

                if (msgCallback != null)
                {
                    msgCallback(msg);
                    return null;
                }

                return MakeParameters(msg);
            }
            else
            {
                var sds = SiteDrive.ParseList(siteDrives);
                if (sds.Length == 0)
                {
                    pipeline.LogInfo("no sitedrives");
                    return null;
                }
                var ret = new ContextualMeshParameters();
                ret.RDRDir = rdrDir;
                ret.PrimarySol = primarySol;
                ret.PrimarySiteDrive = sds[0];
                ret.Extent = extent;
                if (orbitalOnly)
                {
                    ret.Sols.Add(primarySol);
                    ret.SiteDrives.Add(sds[0]);
                }
                else
                {
                    ret.Sols.UnionWith(allSols);
                    ret.SiteDrives.UnionWith(sds);
                }
                return ret;
            }
        }

        private void BuildContextualTileset()
        {
            var parameters = MakeBatchParameters();
            if (parameters != null)
            {
                BuildContextualTileset(parameters);
            }
        }

        public enum TilesetStatus { absent, found, done, processing, zombie };
        private TilesetStatus CheckForTileset(ContextualMeshMessage msg, string destDir, int version,
                                              int? overrideSol = null)
        {
            string project = string.Format("{0}_{1}{2}{3}", SolToString(overrideSol ?? msg.primarySol),
                                           msg.primarySiteDrive.ToString(),
                                           version > 0 ? "V" + version.ToString("D2") : "",
                                           msg.orbitalOnly ? "_orbital" : "");
            string url = $"{destDir}/{project}/";
            pipeline.LogVerbose("checking for tileset under {0}", url);
            bool absent = true, sameMsg = false, hasTileset = false;
            foreach (var f in SearchFiles(url, recursive: false))
            {
                absent = false;
                if (f.EndsWith(TILESET_JSON))
                {
                    hasTileset = true;
                }
                else if (f.EndsWith(MESSAGE_JSON))
                {
                    try
                    {
                        var existingJson = File.ReadAllText(GetFile(f, filenameUnique: false));
                        var existingMsg = JsonHelper.FromJson<ContextualMeshMessage>(existingJson);
                        if (existingMsg.SameTileset(msg, pipeline))
                        {
                            sameMsg = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        pipeline.LogException(ex, "failed to read " + f);
                    }
                }
                else if (f.EndsWith(PID_JSON))
                {
                    try
                    {
                        var existingJson = File.ReadAllText(GetFile(f, filenameUnique: false));
                        var existingContent = JsonHelper.FromJson<ContextualPIDContent>(existingJson);
                        var existingMsg = existingContent.message;
                        if (existingMsg.SameTileset(msg, pipeline))
                        {
                            DateTime? ts = null;
                            int zs = options.ZombieSec;
                            if (zs < 0 ||
                                DateTime.Now.Subtract((ts = storageHelper.LastModified(f)).Value).TotalSeconds < zs)
                            {
                                return TilesetStatus.processing;
                            }
                            else
                            {
                                return TilesetStatus.zombie;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        pipeline.LogException(ex, "failed to read " + f);
                    }
                }
            }
            return (sameMsg && hasTileset) ? TilesetStatus.done : absent ? TilesetStatus.absent : TilesetStatus.found;
        }

        private class ContextualPIDContent : ServicePIDContent
        {
            public ContextualMeshMessage message;

            public ContextualPIDContent(string pid, string status, QueueMessage msg) : base(pid, status, msg)
            {
                this.message = msg as ContextualMeshMessage;
            }
        }

        protected override string MakePIDContent(string pid, string status)
        {
            return JsonHelper.ToJson(new ContextualPIDContent(pid, status, currentMessage), autoTypes: false);
        }

        private string AssignVersionAndSavePID(string destDir, ref string project, bool force)
        {
            string projSfx = "_orbital";
            if (project.EndsWith(projSfx))
            {
                project = project.Substring(0, project.Length - projSfx.Length);
            }
            else
            {
                projSfx = "";
            }

            string version = "";
            string versionedProject = project + version + projSfx;

            string pid = GetPID();
            string pidFile = null;
            if (!string.IsNullOrEmpty(options.TilesetVersion))
            {
                if (int.TryParse(options.TilesetVersion, out int v))
                {
                    if (v > 0) { //leave version unset for non-positive integer
                        version = "V" + v.ToString("D2");
                    }
                } else { //non-integer version 
                    version = "V" + options.TilesetVersion;
                }
                versionedProject = project + version + projSfx;
                pidFile = SavePID(destDir, versionedProject, "interlock");
            }
            else
            {
                for (int i = 0; i <= MAX_VERSION && pidFile == null; i++)
                {
                    if (i >= MAX_VERSION)
                    {
                        throw new Exception($"no versions available for tileset {destDir}/{project + projSfx}");
                    }
                    if (i > 0)
                    {
                        version = "V" + i.ToString("D2");
                    }
                    versionedProject = project + version + projSfx;
                    var status = CheckForTileset(currentMessage as ContextualMeshMessage, destDir, i);
                    if (status == TilesetStatus.done || status == TilesetStatus.processing)
                    {
                        if (!force)
                        {
                            pipeline.LogInfo("aborting {0}, already {1} at {2}/{3}",
                                             project, status, destDir, versionedProject);
                            return null;
                        }
                        else
                        {
                            pipeline.LogInfo("force rebuilding {0} with a new version, already {1} at {2}/{3}",
                                             project, status, destDir, versionedProject);
                            status = TilesetStatus.found;
                        }
                    }
                    if (status == TilesetStatus.found)
                    {
                        pipeline.LogInfo("tileset {0}/{1} found, skipping version", destDir, versionedProject);
                        continue;
                    }
                    if (status == TilesetStatus.zombie)
                    {
                        pipeline.LogInfo("tileset {0}/{1} zombie, skipping version", destDir, versionedProject);
                        continue;
                    }
                    pidFile = SavePID(destDir, versionedProject, "interlock");
                    pipeline.LogInfo("beginning {0} version interlock for tileset {1}/{2} for worker {3}",
                                     Fmt.HMS(VERSION_INTERLOCK_SEC * 1e3), destDir, versionedProject, pid);
                    if (!SleepSec(VERSION_INTERLOCK_SEC))
                    {
                        DeletePID(destDir, versionedProject, pidFile);
                        pipeline.LogWarn("tileset {0}/{1} aborted in version interlock", destDir, versionedProject);
                        return null;
                    }
                    string pfx = versionedProject + "_";
                    string sfx = "_" + PID_JSON;
                    string pattern = pfx + "*" + sfx;
                    var pids =
                        SearchFiles($"{destDir}/{versionedProject}/", globPattern: pattern, recursive: false)
                        .Select(url => StringHelper.GetLastUrlPathSegment(url))
                        .Select(n => n.Substring(0, n.Length - sfx.Length).Substring(pfx.Length))
                        .OrderByDescending(p => p)
                        .ToList();
                    if (pids.Count == 0)
                    {
                        DeletePID(destDir, versionedProject, pidFile); //just in case
                        pipeline.LogWarn("tileset {0}/{1} aborted in version interlock, PID file missing",
                                         destDir, versionedProject);
                        return null;
                    }
                    if (!pids[0].Equals(pid))
                    {
                        //we are not the highest PID attempting to build this version, abort
                        DeletePID(destDir, versionedProject, pidFile);
                        pipeline.LogInfo("tileset {0}/{1} aborted in version interlock, claimed by worker {2}",
                                         destDir, versionedProject, pids[0]);
                        return null;
                    }
                    pipeline.LogInfo("tileset {0}/{1} claimed by worker {2}", destDir, versionedProject, pid);
                }
            }

            pipeline.LogInfo("using version \"{0}\" for tileset {1}/{2}{3}", version, destDir, project, projSfx);

            project = versionedProject;

            return pidFile;
        }

        private string SavePID(string destDir, string project, Phase phase, string pidFile)
        {
            return SavePID(destDir, project, phase.ToString(), pidFile);
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
        private void BuildContextualTileset(ContextualMeshParameters p)
        {
            CheckCredentials(force: true);

            pipeline.LogInfo(DumpParameters(p, verbose: true));

            string rdrDir = p.RDRDir ?? options.RDRDir;
            if (String.IsNullOrEmpty(rdrDir))
            {
                throw new ArgumentException("rdrDir empty");
            }
            rdrDir = StringHelper.NormalizeUrl(rdrDir, preserveTrailingSlash: false);

            int primarySol = p.PrimarySol;
            HashSet<int> sols = p.Sols;
            if (!sols.Contains(primarySol))
            {
                throw new ArgumentException("sols must contain primarySol");
            }
            RemoveBlacklistedSols(sols, primarySol); //also enforces solRange

            SiteDrive primarySiteDrive = p.PrimarySiteDrive;
            HashSet<SiteDrive> siteDrives = p.SiteDrives;
            if (!siteDrives.Contains(primarySiteDrive))
            {
                throw new ArgumentException("siteDrives must contain primarySiteDrive");
            }

            bool orbitalOnly = p.OrbitalOnly || options.NoSurface;
            string missionStr = mission != null ? mission.GetMission().ToString() : "None";
            string fullMissionStr = mission != null ? mission.GetMissionWithVenue() : "None";
            string sdStr = primarySiteDrive.ToString();
            string solStr = SolToString(primarySol, forceNumeric: true);
            string sdsStr = string.Join(",", siteDrives.ToArray());
            string solRanges = MakeSolRanges(sols);
            string project = string.Format("{0}_{1}{2}", SolToString(primarySol), sdStr, orbitalOnly ? "_orbital" : "");
            string venue = string.Format("contextual_{0}_{1}", missionStr, project);
            string venueDir = storageDir + "/" + venue;
            string solDir = StringHelper.ReplaceIntWildcards(rdrDir, primarySol);
            string fetchDir = !string.IsNullOrEmpty(options.FetchDir) ? options.FetchDir : storageDir + "/" + FETCH_DIR;
            string fetchExclude =
                !string.IsNullOrEmpty(options.VCEPattern) ? $"--excludepattern={options.VCEPattern}" : "";
            string ingestDir = rdrDir.StartsWith("s3://") ? (fetchDir + "/rdrs") : solDir;
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
            string noSurface = orbitalOnly ? "--nosurface" : "";
            string orbitalDEMFileOpt = !string.IsNullOrEmpty(orbitalDEMFile) ? $"--orbitaldem={orbitalDEMFile}" : null;
            string orbitalImageFileOpt =
                !string.IsNullOrEmpty(orbitalImageFile) ? $"--orbitalimage={orbitalImageFile}" : null;
            string camerasOpt =
                !string.IsNullOrEmpty(options.OnlyForCameras) ? $"--onlyforcameras={options.OnlyForCameras}" : null;
            string allowUnmasked = options.AllowUnmaskedRoverObservations ? "--allowunmaskedroverobservations" : null;
            string colorize = options.Colorize ? "--colorize" : null;
            string extent = p.Extent > 0 ? p.Extent.ToString() : options.Extent.ToString();
            string surfaceExtent = options.SurfaceExtent.ToString(); 

            //when we save the message json to s3 include the actual effective extent
            //this really only matters in corner cases where a legacy message without explicit extent was received
            var cmm = currentMessage as ContextualMeshMessage;
            if (cmm != null && cmm.extent <= 0)
            {
                cmm.extent = p.Extent > 0 ? p.Extent : options.Extent;
            }

            pipeline.LogInfo("building contextual tileset {0} from {1} sitedrives in {2} sols",
                             project, siteDrives.Count, sols.Count);
            try
            {
                Cleanup(venueDir);

                Configure(venue);

                bool force = (cmm != null && cmm.force) ||
                    (orbitalOnly ? options.RecreateExistingOrbital : !options.NoRecreateExistingContextual);
                string pidFile = AssignVersionAndSavePID(destDir, ref project, force);
                if (string.IsNullOrEmpty(pidFile))
                {
                    return;
                }

                SaveMessage(destDir, project);

                string tilesetDir = venueDir + "/" + TilingCommand.TILESET_DIR + "/" + project;

                if (!options.NoFetch && !orbitalOnly && rdrDir.StartsWith("s3://") &&
                    options.StartPhase <= Phase.fetch && options.EndPhase >= Phase.fetch)
                {
                    string searchLocations = rdrDir + "/";
                    string[] rdrSubdirs = mission.GetContextualMeshRDRSubdirs();
                    if (rdrSubdirs != null && rdrSubdirs.Length > 0)
                    {
                        searchLocations = string.Join(",", rdrSubdirs.Select(d => $"{rdrDir}/{d}/"));
                    }
                    Fetch(options.MaxFetch, solRanges, ingestDir, searchLocations,
                          "--onlyforsitedrives", sdsStr, fetchExclude, "--nomeshes", "--summary");
                }

                if (!options.NoFetch && !options.NoOrbital &&
                    options.StartPhase <= Phase.fetch && options.EndPhase >= Phase.fetch)
                {
                    Action<string, string> fetchOrbitalAsset = (url, file) =>
                    {
                        if (!string.IsNullOrEmpty(url) && !string.IsNullOrEmpty(file))
                        {
                            if (!File.Exists(file))
                            {
                                pipeline.LogInfo("fetching orbital asset {0} -> {1}", url, file);
                                string dir = Path.GetDirectoryName(file);
                                try
                                {
                                    Fetch(options.MaxOrbital, url, dir, "--raw", "--nosubdirs");
                                    string srcFile = StringHelper.GetLastUrlPathSegment(url);
                                    string destFile = Path.GetFileName(file);
                                    string fetchedFile = Path.Combine(dir, srcFile);
                                    if (srcFile != destFile && File.Exists(fetchedFile))
                                    {
                                        PathHelper.MoveFileAtomic(fetchedFile, file); //overwrites existing
                                    }
                                }
                                catch (Exception ex)
                                {
                                    pipeline.LogException(ex, "error fetching orbital asset " + url);
                                    //swallow exception and continue without it
                                    //IngestAlignmentInputs will also spew an error message but continue
                                    //the contextual mesh should still build but without the orbital asset(s)
                                }
                            }
                            else
                            {
                                pipeline.LogInfo("using cached orbital asset {0} -> {1}", url, file);
                            }
                        }
                    };
                    //fetch the DEM after the image just in case they both can't fit within the MaxOrbital limit
                    //in which case the DEM should win
                    //TODO it would probably be better to download both with one call to fetch
                    //but currently if we were to do that fetch would prioritize in order of their server timestamps
                    CheckCredentials();
                    fetchOrbitalAsset(orbitalImageUrl, orbitalImageFile);
                    fetchOrbitalAsset(orbitalDEMUrl, orbitalDEMFile);
                }

                if (!options.NoIngest && options.StartPhase <= Phase.ingest && options.EndPhase >= Phase.ingest)
                {
                    if (sols.Count > 1 && ingestDir.StartsWith("s3://") && ingestDir == solDir && ingestDir != rdrDir)
                    {
                        throw new NotImplementedException("ingestion from multi-sol s3 wildcard not implemented");
                    }
                    SavePID(destDir, project, Phase.ingest, pidFile);
                    CheckCredentials();
                    RunCommand("ingest", project, "--mission", fullMissionStr, "--meshframe", sdStr, 
                               "--onlyforsitedrives", sdsStr, "--onlyforsols", solRanges,
                               "--inputpath", ingestDir + "/" + (options.RecursiveSearch ? "**" : "*"),
                               noSurface, noOrbital, orbitalDEMFileOpt, orbitalImageFileOpt, camerasOpt);
                }

                if (!options.NoAlign && !orbitalOnly &&
                    options.StartPhase <= Phase.align && options.EndPhase >= Phase.align)
                {
                    SavePID(destDir, project, Phase.align, pidFile);
                    CheckCredentials();
                    RunCommand("bev-align", options.AbortOnAlignmentError, project, allowUnmasked);
                    CheckCredentials();
                    RunCommand("heightmap-align", options.AbortOnAlignmentError, project, allowUnmasked);
                }

                if (!options.NoGeometry && options.StartPhase <= Phase.geometry && options.EndPhase >= Phase.geometry)
                {
                    SavePID(destDir, project, Phase.geometry, pidFile);
                    CheckCredentials();
                    RunCommand("build-geometry", project, "--extent", extent, "--surfaceextent", surfaceExtent,
                               allowUnmasked);
                }
                
                if (!options.NoTileset)
                {
                    if (options.StartPhase <= Phase.leaves && options.EndPhase >= Phase.leaves)
                    {
                        SavePID(destDir, project, Phase.leaves, pidFile);
                        CheckCredentials();
                        BuildTilingInput(project, allowUnmasked, options.Redo ? "--texturevariant=Original" : "");
                    }

                    if (!orbitalOnly && options.StartPhase <= Phase.blend && options.EndPhase >= Phase.blend)
                    {
                        SavePID(destDir, project, Phase.blend, pidFile);
                        CheckCredentials();
                        RunCommand("blend-images", project, allowUnmasked, colorize);
                    }

                    if (options.StartPhase <= Phase.tileset && options.EndPhase >= Phase.tileset)
                    {
                        SavePID(destDir, project, Phase.tileset, pidFile);
                        CheckCredentials();
                        BuildTileset(project, allowUnmasked);
                    }
                    
                    if (options.StartPhase <= Phase.manifest && options.EndPhase >= Phase.manifest)
                    {
                        SavePID(destDir, project, Phase.manifest, pidFile);
                        CheckCredentials();
                        RunCommand("update-scene-manifest", project, "--notactical", "--nourls", "--nosky",
                                   allowUnmasked, "--sol", solStr, "--sitedrive", sdStr, "--sols", solRanges,
                                   "--sitedrives", sdsStr, "--manifestfile", tilesetDir + "/" + SCENE_JSON);
                    }
                        
                    if (options.StartPhase <= Phase.save && options.EndPhase >= Phase.save)
                    {
                        SavePID(destDir, project, Phase.save, pidFile);
                        CheckCredentials();
                        SaveTileset(tilesetDir, project, destDir);
                    }

                    if (!options.NoSky && !orbitalOnly &&
                        options.StartPhase <= Phase.sky && options.EndPhase >= Phase.sky)
                    {
                        SavePID(destDir, project, Phase.sky, pidFile);
                        CheckCredentials();
                        RunCommand("build-sky-sphere", project, "--skymode", options.SkyMode.ToString(),
                                   allowUnmasked, "--sphereradius", options.SkySphereRadius,
                                   "--minbackprojectradius", options.SkyMinBackprojectRadius);
                        string skyTilesetDir = venueDir + "/" + BuildSkySphere.SKY_TILESET_DIR + "/" + project;
                        CheckCredentials();
                        SaveTileset(skyTilesetDir, project + "_sky", destDir);
                    }
                }

                if (!options.NoCombinedManifest && !orbitalOnly &&
                    options.StartPhase <= Phase.combinedManifest && options.EndPhase >= Phase.combinedManifest)
                {
                    SavePID(destDir, project, Phase.combinedManifest, pidFile);
                    CheckCredentials();
                    RunCommand("update-scene-manifest", project, allowUnmasked, "--tilesetdir", destDir,
                               "--rdrdir", rdrDir, "--sol", solStr, "--sitedrive", sdStr,
                               "--sols", solRanges, "--sitedrives", sdsStr,
                               "--awsprofile", awsProfile, "--awsregion", awsRegion);
                }

                DeletePID(destDir, project, pidFile);

                Cleanup(venueDir);

                FetchData.ExpireEDRCache(msg => pipeline.LogInfo(msg));
            }
            catch (Exception ex)
            {
                pipeline.LogError("fatal error producing contextual tileset {0}", project);
                pipeline.LogException(ex); 
                Cleanup(venueDir);
                throw;
            }
        }

        private string FilterProduct(RoverProductId id)
        {
            if (!(id is OPGSProductId))
            {
                return "not an OPGS product ID";
            }
            //MSL OPGS single frame product IDs don't actually have sol number in them
            //though they do have SCLK, but we don't currently derive a sol from that
            if (id.HasSol() && solBlacklist.Contains(id.GetSol()))
            {
                return "blacklisted sol";
            }
            return null;
        }

        private string FilterWedge(RoverProductId id, string url)
        {
            return FilterProduct(id) ?? mission.FilterContextualMeshWedge(id, url);
        }

        //uses S3, so needs to hold credentialRefreshLock when not running from ServiceLoop()
        //actually called from
        //ServiceLoop() -> AcceptMessage()
        //ServiceLoop() -> HandleMessage()
        //EnqueueContextualMessages() (locks credentialRefreshLock) ->  LoadSiteDriveLists() [-> MakeList()]
        //MakeContextualMeshParameters() -> FindAllSiteDrives() [-> MakeList()] (no lock)
        private string FilterTexture(RoverProductId id, string url)
        {
            bool videoEDRExists(string s3Folder, string basename)
            {
                //only use cached folder listings in batch mode, i.e. when manually running a contextual mesh
                //the master and worker services still use an EDR existence cache but don't list the whole EDR folder
                //rather, they list and cache (for up to 2 days) each EDR product individually as needed
                //because in some circumstances more EDRs could still arrive in the folder after the first listing
                return FetchData.VideoEDRExists(s3Folder, basename, mission, storageHelper,
                                                cacheFolderListings: !serviceMode,
                                                info: msg => pipeline.LogInfo(msg),
                                                verbose: msg => pipeline.LogVerbose(msg),
                                                warn: msg => pipeline.LogWarn(msg));
            }
            return FilterProduct(id) ?? mission.FilterContextualMeshTexture(id, url) ??
                (mission.IsVideoProduct(id, url, videoEDRExists) ? "excluded video product" : null);
        }

        private string FilterFDR(RoverProductId id, string url)
        {
            return FilterProduct(id);
        }

        //RRR_[TTTT]SSSDDDD[I][_VV].lis
        //where RRR is e.g. xyz, xym, iv, etc; TTTT is sol; SSS site; DDDD drive; I instrument; VV version
        private static readonly Regex LIST_FILENAME_REGEX =
            new Regex(@"[^_]+_(?:\d{4})?([0-9A-Z]{7})[A-Z]?(?:_[0-9A-Z]{1,2})?\.lis$", RegexOptions.IgnoreCase);

        private SiteDrive? GetSiteDrive(string url, RoverProductId id = null)
        {
            if (id == null)
            {
                var idStr = StringHelper.GetLastUrlPathSegment(url, stripExtension: true);
                id = RoverProductId.Parse(idStr, mission, throwOnFail: false);
            }
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

        //called by
        //HandleMessage()
        //LoadSiteDriveLists()
        private void LoadList(SiteDriveList sdList, String url)
        {
            int split = url.LastIndexOf("/ids-pipeline/");
            string baseUrl = split > 0 ? url.Substring(0, split) : ("s3://" + (new S3Url(url).BucketName));
            sdList.LoadListFile(GetFile(url), baseUrl);
        }

        private SiteDriveList MakeList(String rdrDir, SiteDrive sd)
        {
            return new SiteDriveList(rdrDir, sd, mission, pipeline, FilterWedge, FilterTexture);
        }

        private SolutionNotificationMessage TryParseSolutionNotification(string txt)
        {
            if (txt == null || !txt.Contains("Message") ||
                !txt.Contains("site") || !txt.Contains("drive") || !txt.Contains("view"))
            {
                return null;
            }
            try
            {
                //pipeline.LogInfo("parsing solution notification\n{0}", txt);
                var msg = JsonHelper.FromJson<SolutionNotificationMessage>(txt, autoTypes: false);
                return (msg != null && msg.Parse()) ? msg : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private bool AssignSolAndRDRDir(SolutionNotificationMessage msg)
        {
            if (msg.AssignedSolOrRDRDir())
            {
                bool valid = msg.HasValidSolAndRDRDir();
                if (valid)
                {
                    pipeline.LogInfo("already assigned valid sol and RDR dir for {0}", msg);
                }
                else
                {
                    pipeline.LogWarn("already assigned invalid sol and/or RDR dir for {0}", msg);
                }
                return valid;
            }

            //unfortunately are lots of apparently bogus sol folders beyond the latest actual real sol on sops
            //so we can't just rely on sorting the output of an s3 ls and taking the last folder
            //instead we just keep track of the higest sol number as we get ObjectCreated messages
            //this is potentially fallible for a few reasons (e.g. an untimely server restart)
            //but in the common case we should typically get at least one ecam FDR in a sol before
            //we get a best_tactical PLACES solution notification due to mapping specialist manual localization

            int latestSol = -1;
            string rdrDir = null;

            var sd = msg.GetSiteDrive();

            lock (latestSolAndRDRDir)
            {
                string sdStr = sd.ToString();
                foreach (string key in new string[] { sdStr, "any" })
                {
                    if (latestSolAndRDRDir.ContainsKey(key))
                    {
                        var val = latestSolAndRDRDir[key];
                        latestSol = val.Sol;
                        rdrDir = val.RDRDir;
                        pipeline.LogInfo("using latest sol {0} and RDR directory {1} for sitedrive \"{2}\" " +
                                         "from S3 notifications for {3}", latestSol, rdrDir, key, msg);
                        break;
                    }
                    else
                    {
                        pipeline.LogWarn("no S3 notifications for sitedrive \"{0}\" " +
                                         "to assign sol and RDR directory for {1}", key, msg);
                    }
                }
            }

            if (latestSol >= 0 && !string.IsNullOrEmpty(rdrDir))
            {
                pipeline.LogInfo("skipping S3 search, " +
                                 "got latest sol {0} and RDR directory {1} from S3 notifications for {2}",
                                 latestSol, rdrDir, msg);
            }
            else if (options.NoSearchForSolContainingSiteDriveOnPLACESNotification)
            {
                pipeline.LogWarn("search disabled and no S3 notifications to assign sol and RDR directory for {0}",
                                 msg);
            }
            else
            {
                pipeline.LogInfo("searching for latest sol and RDR directory containing sitedrive for {0}", msg);

                var fdrSearchDirs = mission.GetFDRSearchDirs();
                if (fdrSearchDirs == null || fdrSearchDirs.Count == 0)
                {
                    pipeline.LogWarn("failed to find RDR dir for {0}, mission has no FDR search dirs", msg);
                    return false;
                }
                fdrSearchDirs = fdrSearchDirs
                    .Select(d => StringHelper.EnsureTrailingSlash(StringHelper.NormalizeUrl(d)))
                    .ToList();
                
                var sols = new HashSet<int>();
                string lastSolSearchDir = null;
                foreach (string fdrSearchDir in fdrSearchDirs) //e.g. s3://BUCKET/ods/VER/sol/#####/ids/fdr/ncam/
                {
                    if (SiteDriveList.GetSolSpan(fdrSearchDir, out int start, out int len))
                    {
                        string solSearchDir = fdrSearchDir.Substring(0, start); //e.g. s3://BUCKET/ods/VER/sol/
                        if (lastSolSearchDir == null || solSearchDir != lastSolSearchDir) {
                            sols.Clear();
                            foreach (var s3Url in storageHelper.SearchObjects(solSearchDir, recursive: false,
                                                                              folders: true, files: false))
                            {
                                var solUrl = StringHelper.NormalizeUrl(s3Url); //e.g. s3://BUCKET/ods/VER/sol/00534/
                                pipeline.LogVerbose("found sol dir {0}", solUrl);
                                string solStr = StringHelper.GetLastUrlPathSegment(solUrl.TrimEnd('/')); //e.g. 00534
                                if (int.TryParse(solStr, out int sol))
                                {
                                    sols.Add(sol);
                                }
                            }
                            lastSolSearchDir = solSearchDir;
                        }

                        //e.g. s3://BUCKET/ods/VER/sol/#####/ids/fdr/
                        string rdrSearchDir = NormalizeRDRDir(SiteDriveList.GetRDRDir(fdrSearchDir));
                        if (!string.IsNullOrEmpty(rdrSearchDir))
                        {
                            foreach (int sol in sols.OrderByDescending(s => s).Where(sol => sol > latestSol).ToList())
                            {
                                //e.g. s3://BUCKET/ods/VER/sol/00534/ids/fdr/ncam/
                                string fdrDir = StringHelper.ReplaceIntWildcards(fdrSearchDir, sol);
                                string pat = !string.IsNullOrEmpty(options.FDRPattern) &&
                                    !string.Equals(options.FDRPattern, "none", StringComparison.OrdinalIgnoreCase) ?
                                    options.FDRPattern : DEF_FDR_PATTERN;
                                string glob = ParseRDRExtension(pat, "FDR");
                                pipeline.LogInfo("searching {0} for {1} to assign sol and RDR dir for {2}",
                                                 fdrDir, glob, msg);
                                bool found = false;
                                foreach (var fdrUrl in SearchFiles(fdrDir, glob, recursive: false))
                                {
                                    var fdrSD = GetSiteDrive(StringHelper.NormalizeUrl(fdrUrl));
                                    if (fdrSD.HasValue && fdrSD.Value == sd)
                                    {
                                        pipeline.LogInfo("found FDR to assign sol {0} and RDR dir {1} for {2}: {3}",
                                                         sol, rdrSearchDir, msg, fdrUrl);
                                        latestSol = sol;
                                        rdrDir = rdrSearchDir;
                                        found = true;
                                        break; //inner loop
                                    }
                                }
                                if (found)
                                {
                                    break; //outer loop
                                }
                            }
                        }
                        else
                        {
                            pipeline.LogWarn("failed to get RDR dir from FDR search dir {0} while getting sol for {1}",
                                             fdrSearchDir, msg);
                        }
                    }
                    else
                    {
                        pipeline.LogWarn("could not find sol span in {0} while getting sol for {1}", fdrSearchDir, msg);
                    }
                }
                
                if (latestSol >= 0 && !string.IsNullOrEmpty(rdrDir))
                {
                    pipeline.LogInfo("using latest sol {0} and RDR directory {1} from S3 search for {2}",
                                     latestSol, rdrDir, msg);
                }
                else
                {
                    pipeline.LogWarn("S3 search failed to assign sol and RDR directory for {0}", msg);
                }
            }

            msg.sol = latestSol;
            msg.rdrDir = rdrDir ?? "none";

            return msg.HasValidSolAndRDRDir();
        }

        private List<string> GetInstDirs(string rdrDir, Func<int, bool> filterSol) 
        {
            string[] subDirs = mission != null ? mission.GetContextualMeshRDRSubdirs() : null;
            var ret = new List<string>();
            if (SiteDriveList.GetSolSpan(rdrDir, out int start, out int len))
            {
                string dir = rdrDir.Substring(0, start); //includes trailing slash
                string sfx = rdrDir.Substring(start + len); //includes beginning slash
                foreach (var s3Url in storageHelper.SearchObjects(dir, recursive: false, folders: true, files: false))
                {
                    var url = StringHelper.NormalizeUrl(s3Url);
                    string solFolder = url.TrimEnd('/');
                    string solStr = StringHelper.GetLastUrlPathSegment(solFolder);
                    if (int.TryParse(solStr, out int sol) && filterSol(sol))
                    {
                        if (subDirs != null)
                        {
                            foreach (var subDir in subDirs)
                            {
                                ret.Add(StringHelper.EnsureTrailingSlash(solFolder + sfx + subDir));
                            }
                        }
                        else
                        {
                            ret.Add(solFolder + sfx);
                        }
                    }
                }
                pipeline.LogInfo("recursively searching {0} instrument directories", ret.Count);
            }
            else
            {
                pipeline.LogWarn("could not find sol span, recursively searching entire RDR dir {0}", rdrDir);
                ret.Add(rdrDir);
            }
            return ret;
        }

        private string MakeWedgeAndTextureRegex(string pdsExts)
        {
            string wr = null;
            if (wedgeRegex != null) //handle "none"
            {
                string wp = StringHelper.StripUrlExtension(options.WedgePattern);
                wr = StringHelper.WildcardToRegularExpressionString(wp, fullMatch: false, matchSlashes: false,
                                                                    allowAlternation: true);
            }
            string tr = null;
            if (textureRegex != null) //handle "none"
            {
                string tp = StringHelper.StripUrlExtension(options.TexturePattern);
                tr = StringHelper.WildcardToRegularExpressionString(tp, fullMatch: false, matchSlashes: false,
                                                                    allowAlternation: true);
            }
            string er = "[.](" + pdsExts.ToUpper().Replace(",","|") + ")";
            if (wr != null && tr != null)
            {
                return $"^.*/({wr}|{tr}){er}$";
            }
            else if (wr != null)
            {
                return $"^.*/{wr}{er}$";
            }
            else if (tr != null)
            {
                return $"^.*/{tr}{er}$";
            }
            else
            {
                return $"^.*/$";
            }
        }

        private Dictionary<SiteDrive, SiteDriveList> FindAllSiteDrives(string rdrDir, int primarySol, HashSet<int> sols)
        {
            var ret = new Dictionary<SiteDrive, SiteDriveList>();

            if (wedgeRegex == null)
            {
                pipeline.LogWarn("wedge pattern empty");
                return ret;
            }

            rdrDir = StringHelper.NormalizeUrl(rdrDir, preserveTrailingSlash: false) + "/"; //prob redundant, but ok

            string solRanges = MakeSolRanges(sols);

            pipeline.LogInfo("finding all sitedrives in RDR dir {0} for sols {1}", rdrDir, solRanges);
            
            var instDirs = GetInstDirs(rdrDir, sol => sols.Contains(sol));
            string pdsExts = mission != null ? mission.GetPDSExts(disablePDSLabelFiles: true) : DEF_PDS_EXTS;
            Regex wr = wedgeRegex != null ? MakeURLRegex(options.WedgePattern, pdsExts) : null;
            Regex tr = textureRegex != null ? MakeURLRegex(options.TexturePattern, pdsExts) : null;
            string wtr = MakeWedgeAndTextureRegex(pdsExts);
            string what = "wedges" + (tr != null ? " and textures" : "") + " matching \"" + wtr + "\"";
            what += " (wedge: \"" + wr + "\"" + (tr != null ? (", texture: \"" + tr + "\"") : "") + ")";
            foreach (string dir in instDirs)
            {
                pipeline.LogInfo("recursively searching {0} for {1}", dir, what);
                int nu = 0, na = 0, nw = 0, nt = 0, ns = 0;
                try
                {
                    var s3Urls = storageHelper.SearchObjects(dir, wtr, recursive: true, patternIsRegex: true).ToList();
                    foreach (var s3Url in FilterURLs(s3Urls, pdsExts))
                    {
                        nu++;
                        string url = StringHelper.NormalizeUrl(s3Url);
                        if (AcceptBucketPath(url))
                        {
                            na++;
                            var idStr = StringHelper.GetLastUrlPathSegment(url, stripExtension: true);
                            var id = RoverProductId.Parse(idStr, mission, throwOnFail: false);
                            bool acceptedWedge = wr.IsMatch(url) && FilterWedge(id, url) == null;
                            bool acceptedTexture = !acceptedWedge && tr != null && tr.IsMatch(url) &&
                                FilterTexture(id, url) == null;
                            if (acceptedWedge || acceptedTexture)
                            {
                                var sd = (id as OPGSProductId).SiteDrive;
                                if (!ret.ContainsKey(sd))
                                {
                                    ret[sd] = MakeList(rdrDir, sd);
                                    ns++;
                                }
                                if (ret[sd].Add(url) == null)
                                {
                                    if (acceptedWedge)
                                    {
                                        nw++;
                                    }
                                    if (acceptedTexture)
                                    {
                                        nt++;
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    pipeline.LogException(ex, $"error searching for sitedrives under {dir}");
                }
                pipeline.LogVerbose("found {0} wedges, {1} textures, {2} new sitedrives from {3} URLs ({4} accepted)",
                                    nw, nt, ns, nu, na);
            }

            //keep only latest version of each product, remove non-preferred stereo eye, non-preferred lin/nonlin, etc
            pipeline.LogInfo("filtering {0} sitedrive wedge lists for sols {1}", ret.Count, solRanges);

            var filtered = new Dictionary<SiteDrive, SiteDriveList>();
            foreach (var sd in ret.Keys.OrderBy(sd => sd))
            {
                if (ret[sd].NumWedges > 0)
                {
                    var filteredSD = ret[sd].FilterProductIDGroups();
                    if (filteredSD.NumWedges > 0)
                    {
                        filtered[sd] = filteredSD;
                        pipeline.LogInfo("filtered sitedrive {0}: sols {1}->{2}, wedges {3}->{4}, textures {5}->{6}",
                                         sd, MakeSolRanges(ret[sd].Sols), MakeSolRanges(filteredSD.Sols),
                                         ret[sd].NumWedges, filteredSD.NumWedges,
                                         ret[sd].NumTextures, filteredSD.NumTextures);
                    }
                    else
                    {
                        pipeline.LogInfo("culled empty filtered sitedrive {0}: sols {1}, {2} wedges, {3} textures",
                                         sd, MakeSolRanges(ret[sd].Sols), ret[sd].NumWedges, ret[sd].NumTextures);
                    }
                }
                else
                {
                    pipeline.LogInfo("culled sitedrive with no wedges {0}: sols {1}, {2} wedges, {3} textures",
                                     sd, MakeSolRanges(ret[sd].Sols), ret[sd].NumWedges, ret[sd].NumTextures);
                }
            }
            int culled = ret.Count - filtered.Count;
            ret = filtered;
            if (culled > 0)
            {
                pipeline.LogInfo("culled {0} sitedrive wedge lists that were empty after filtering, {1} remain",
                                 culled, ret.Count);
            }

            pipeline.LogInfo("found {0} sitedrives for sols {1}", ret.Count, solRanges);
            foreach (var sd in ret.Keys.OrderBy(sd => sd))
            {
                pipeline.LogInfo("{0}: sols {1}, {2} wedges, {3} textures",
                                 sd, MakeSolRanges(ret[sd].Sols), ret[sd].NumWedges, ret[sd].NumTextures);
            }

            return ret;
        }

        //called by EnqueueContextualMessages()
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

            //keep only latest version of each product, remove non-preferred stereo eye, non-preferred lin/nonlin, etc
            void filterLists(bool passthroughEmpty)
            {
                pipeline.LogInfo("filtering {0} sitedrive wedge lists", ret.Count);
                var filtered = new Dictionary<SiteDrive, Stamped<SiteDriveList>>();
                foreach (var sd in ret.Keys.OrderBy(sd => sd))
                {
                    if (ret[sd].Value.NumWedges > 0)
                    {
                        var filteredSD = ret[sd].Value.FilterProductIDGroups();
                        if (filteredSD.NumWedges > 0)
                        {
                            filtered[sd] = new Stamped<SiteDriveList>(filteredSD, ret[sd].Timestamp);
                            pipeline.LogInfo("filtered sitedrive {0}: " +
                                             "sols {1}->{2}, wedges {3}->{4}, textures {5}->{6}",
                                             sd, MakeSolRanges(ret[sd].Value.Sols), MakeSolRanges(filteredSD.Sols),
                                             ret[sd].Value.NumWedges, filteredSD.NumWedges,
                                             ret[sd].Value.NumTextures, filteredSD.NumTextures);
                        }
                        else
                        {
                            var sdl = ret[sd].Value;
                            pipeline.LogInfo("culled empty filtered sitedrive {0}: sols {1}, {2} wedges, {3} textures",
                                             sd, MakeSolRanges(sdl.Sols), sdl.NumWedges, sdl.NumTextures);
                        }
                    }
                    else if (passthroughEmpty)
                    {
                        filtered[sd] = ret[sd];
                    }
                    else
                    {
                        var sdl = ret[sd].Value;
                        pipeline.LogInfo("culled sitedrive with no wedges {0}: sols {1}, {2} wedges, {3} textures",
                                         sd, MakeSolRanges(sdl.Sols), sdl.NumWedges, sdl.NumTextures);
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

            foreach (var sd in urls.Keys.OrderBy(sd => sd))
            {
                var sdList = MakeList(rdrDir, sd);
                long latestTimestamp = -1;
                foreach (var entry in urls[sd])
                {
                    string url = entry.Key;
                    if (!FileExists(url))
                    {
                        pipeline.LogWarn("file {0} not found", url);
                        continue;
                    }
                    if (listRegex != null && listRegex.IsMatch(url))
                    {
                        LoadList(sdList, url);
                        listDirs.Add(StringHelper.StripLastUrlPathSegment(url) + "/");
                        listURLs.Add(url);
                    }
                    else if (wedgeRegex != null && wedgeRegex.IsMatch(url))
                    {
                        sdList.Add(url);
                        wedgeURLs.Add(url);
                    }
                    else if (textureRegex != null && textureRegex.IsMatch(url))
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
                if (sdList.NumSols > 0) //not NumWedges to handle case of only texture updates
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

            if (ret.Count == 0)
            {
                //would get InvalidOperationException trying to find minPrimarySol below
                pipeline.LogInfo("culled all changed sitedrives");
                return ret;
            }

            if (pipeline.Verbose)
            {
                foreach (var sd in ret.Keys.OrderBy(sd => sd))
                {
                    pipeline.LogVerbose("changed sitedrive {0}: {1} sols, {2} wedges {3} textures before additions" +
                                        "{4}{5}",
                                        sd, ret[sd].Value.NumSols, ret[sd].Value.NumWedges, ret[sd].Value.NumTextures,
                                        ret[sd].Value.NumIDs > 0 ? ":\n  " : "",
                                        string.Join("\n  ", ret[sd].Value.IDToURL.Values.OrderBy(url => url)));
                }
            }

            int minPrimarySol = ret.Values.Select(sl => sl.Value.MinSol).Min();
            int maxPrimarySol = ret.Values.Select(sl => sl.Value.MaxSol).Max();
            int minSol = Math.Max(0, minPrimarySol - solRange), maxSol = maxPrimarySol + solRange;

            pipeline.LogInfo("primary sol range {0}-{1}, sol search range {2}-{3}",
                             minPrimarySol, maxPrimarySol, minSol, maxSol);

            bool reFilter = false;

            if (!options.NoSearchForAdditionalLists && listRegex != null)
            {
                int additionalLists = 0, additionalSitedrives = 0;

                foreach (var listDir in listDirs)
                {
                    pipeline.LogInfo("searching for additional list files in {0}", listDir);
                    foreach (var listFile in SearchFiles(listDir, options.ListPattern, recursive: false))
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
                                var sdList = ret.ContainsKey(sd.Value) ?
                                    ret[sd.Value].Value : MakeList(rdrDir, sd.Value);
                                LoadList(sdList, url);
                                sdList = sdList.FilterToSolRange(minSol, maxSol);
                                if (ret.ContainsKey(sd.Value))
                                {
                                    ret[sd.Value] = new Stamped<SiteDriveList>(sdList, ret[sd.Value].Timestamp);
                                }
                                else if (sdList.NumWedges > 0)
                                {
                                    ret[sd.Value] = new Stamped<SiteDriveList>(sdList);
                                    additionalSitedrives++;
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

                pipeline.LogInfo("loaded {0} additional lists, {1} additional sitedrives",
                                 additionalLists, additionalSitedrives);
            }

            if (!options.NoSearchForAdditionalWedges && (wedgeRegex != null || textureRegex != null))
            {
                int additionalWedges = 0, additionalTextures = 0, additionalSitedrives = 0;

                var instDirs = GetInstDirs(rdrDir, sol => (sol >= minSol && sol <= maxSol));
                string pdsExts = mission != null ? mission.GetPDSExts(disablePDSLabelFiles: true) : DEF_PDS_EXTS;
                Regex wr = wedgeRegex != null ? MakeURLRegex(options.WedgePattern, pdsExts) : null;
                Regex tr = textureRegex != null ? MakeURLRegex(options.TexturePattern, pdsExts) : null;
                string wtr = MakeWedgeAndTextureRegex(pdsExts);
                string what =
                    (wr != null && tr != null) ?
                    $"wedges and textures matching \"{wtr}\" (wedge: \"{wr}\", texture: \"{tr}\")" :
                    wr != null ? $"wedges matching \"{wtr}\" (\"{wr}\")" :
                    tr != null ? $"textures matching \"{wtr}\" (\"{tr}\")" :
                    "nothing"; //should not get here since at least one of wedgeRegex and/or textureRegex is non-null
                pipeline.LogInfo("searching for additional {0} in RDR dir {1} for sols {2}-{3}",
                                 what, rdrDir, minSol, maxSol);
                foreach (string dir in instDirs)
                {
                    try
                    {
                        pipeline.LogInfo("recursively searching {0} for {1}", dir, what);
                        var s3Urls =
                            storageHelper.SearchObjects(dir, wtr, recursive: true, patternIsRegex: true).ToList();
                        foreach (var s3Url in FilterURLs(s3Urls, pdsExts))
                        {
                            string url = StringHelper.NormalizeUrl(s3Url);
                            if (AcceptBucketPath(url))
                            {
                                var idStr = StringHelper.GetLastUrlPathSegment(url, stripExtension: true);
                                var id = RoverProductId.Parse(idStr, mission, throwOnFail: false);
                                bool acceptedWedge = wr != null && wr.IsMatch(url) &&
                                    FilterWedge(id, url) == null && !wedgeURLs.Contains(url);
                                bool acceptedTexture = !acceptedWedge && tr != null && tr.IsMatch(url)
                                    && FilterTexture(id, url) == null && !textureURLs.Contains(url);
                                if (acceptedWedge || acceptedTexture)
                                {
                                    var sd = (id as OPGSProductId).SiteDrive;
                                    if (!ret.ContainsKey(sd))
                                    {
                                        ret[sd] = new Stamped<SiteDriveList>(MakeList(rdrDir, sd));
                                        additionalSitedrives++;
                                    }
                                    var sdl = ret[sd].Value;
                                    int nw = sdl.NumWedges, nt = sdl.NumTextures;
                                    if (acceptedWedge && sdl.Add(url) == null && sdl.NumWedges > nw)
                                    {
                                        //this new URL might still get filtered out below
                                        //e.g. if it's an older version of something that's already in the list
                                        pipeline.LogVerbose("found additional wedge {0} in sitedrive {1}", url, sd);
                                        additionalWedges++;
                                    }
                                    else if (acceptedTexture && sdl.Add(url) == null && sdl.NumTextures > nt)
                                    {
                                        pipeline.LogVerbose("found additional texture {0} in sitedrive {1}", url, sd);
                                        additionalTextures++;
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        pipeline.LogException(ex, $"error searching for {what} under {dir}");
                    }
                }

                reFilter |= additionalWedges > 0 || additionalTextures > 0;

                pipeline.LogInfo("found {0} additional wedges, {1} additional textures, {2} additional sitedrives",
                                 additionalWedges, additionalTextures, additionalSitedrives);
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
                    pipeline.LogVerbose("{0}changed sitedrive {1}: {2} sols, {3} wedges, {4} textures after additions" +
                                        "{5}{6}",
                                        changedSDs.Contains(sd) ? "" : "un", sd, ret[sd].Value.NumSols,
                                        ret[sd].Value.NumWedges, ret[sd].Value.NumTextures,
                                        ret[sd].Value.NumIDs > 0 ? ":\n  " : "",
                                        string.Join("\n  ", ret[sd].Value.IDToURL.Values.OrderBy(url => url)));
                }
            }

            pipeline.LogInfo("{0} filtered changed sitedrives: {1}", changedSDs.Count, String.Join(",", changedSDs));

            return ret;
        }

        // searches for extent overrides in parameter store in order from more specific to less specific
        //
        // {base}/{contextual,orbital}/{sol}/{site}{drive}/extent
        // {base}/{contextual,orbital}/{sol}/{site}/{drive}/extent
        // {base}/{contextual,orbital}/{sol}_{site}{drive}/extent
        // {base}/{contextual,orbital}/{site}{drive}/extent
        // {base}/{contextual,orbital}/site/{site}/drive/{drive}/extent
        // {base}/{contextual,orbital}/site/{site}/extent
        // {base}/{contextual,orbital}/sol/{sol}/extent
        // {base}/{contextual,orbital}/extent
        //
        // {base} defaults to /m20/{venue}/ids/landform but can be overridden
        // M20 deployments typically override by adding a /dyn suffix
        private double GetExtent(int primarySol, SiteDrive primarySD, String service = "contextual")
        {
            if (!IsService())
            {
                return options.Extent;
            }

            string shortSol = primarySol.ToString();
            string canonicalSol = SolToString(primarySol);
            string pathSol = SolToString(primarySol, forceNumeric: true);
            var solStrs = new List<string> { canonicalSol, pathSol, shortSol };

            string sdStr = primarySD.ToString();
            string shortSite = primarySD.Site.ToString();
            string canonicalSite = primarySD.SiteToString();
            string shortDrive = primarySD.Drive.ToString();
            string canonicalDrive = primarySD.DriveToString();
            var sdStrs = new List<String> { sdStr, $"{canonicalSite}/{canonicalDrive}", $"{shortSite}/{shortDrive}" };

            var keys = new List<string>();
            foreach (string sol in solStrs)
            {
                foreach (var sd in sdStrs)
                {
                    keys.Add($"{sol}/{sd}/");
                }
            }
            keys.Add($"{canonicalSol}_{sdStr}/");
            keys.Add($"{sdStr}/");
            keys.Add($"site/{canonicalSite}/drive/{canonicalDrive}/");
            keys.Add($"site/{shortSite}/drive/{shortDrive}/");
            keys.Add($"site/{canonicalSite}/");
            keys.Add($"site/{shortSite}/");
            keys.Add($"sol/{canonicalSol}/");
            keys.Add($"sol/{pathSol}/");
            keys.Add($"sol/{shortSol}/");
            keys.Add(""); //not even the trailing slash
            //19 total keys

            string keyBase = mission.GetServiceSSMKeyBase();
            var checkedKeys = new List<string>();
            foreach (string keyPath in keys)
            {
                string key = keyPath + "extent"; //keyPath has trailing slash iff nonempty
                string overrideExtent = GetParameter(service, key);
                if (overrideExtent != null)
                {
                    if (double.TryParse(overrideExtent, out double e) && e > 0)
                    {
                        pipeline.LogInfo("using extent {0}m from {1}/{2}/{3} (default {4}m)",
                                         e, keyBase, service, key, options.Extent);
                        return e;
                    }
                    else
                    {
                        pipeline.LogWarn("error parsing override extent \"{0}\" from \"{1}\" as a positive number",
                                         overrideExtent, key);
                    }
                }
                checkedKeys.Add($"{keyBase}/{service}/{key}");
            }

            pipeline.LogInfo($"using default extent {options.Extent}m, no override extent in SSM keys:\n  " +
                             string.Join("\n  ", checkedKeys));

            return options.Extent;
        }

        private ContextualMeshMessage MakeOrbitalMeshMessage(SiteDriveList primarySDList)
        {
            return MakeOrbitalMeshMessage(primarySDList.RDRDir, primarySDList.MaxSol, primarySDList.SiteDrive);
        }

        private ContextualMeshMessage MakeOrbitalMeshMessage(string rdrDir, int primarySol, SiteDrive primarySD)
        {
            return new ContextualMeshMessage()
            {
                rdrDir = rdrDir,
                primarySol = primarySol,
                primarySiteDrive = primarySD.ToString(),
                sols = primarySol.ToString(), 
                siteDrives = primarySD.ToString(),
                numWedges = 0,
                orbitalOnly = true,
                timestamp = (long)UTCTime.NowMS(),
                extent = !options.NoAllowOverrideExtent ? GetExtent(primarySol, primarySD, "orbital") : options.Extent
            };
        }

        private ContextualMeshMessage MakeContextualMeshMessage(string rdrDir, int primarySol, SiteDrive primarySD)
        {
            ContextualMeshMessage cmm = null;
            bool orbitalOnly = false;
            var extent = !options.NoAllowOverrideExtent ? GetExtent(primarySol, primarySD) : options.Extent;
            int minSol = Math.Max(0, primarySol - solRange), maxSol = primarySol + solRange;
            MakeContextualMeshParameters(rdrDir,$"{primarySol},{minSol}-{maxSol}", $"{primarySD},auto", orbitalOnly,
                                         extent, msg => cmm = msg);
            return cmm;
        }

        /// <summary>
        /// Applys heruistics to possibly make a ContextualMesh for primarySD.
        /// Returns null if it decided not to make one, or if there was a problem.
        /// If placesDB is null only the primary sitedrive is included
        /// (unless options.MaxSiteDriveDistance <= 0, which disables filtering by distance)
        /// Similarly, if options.MaxSiteDriveDistance > 0 and PlacesDB fails to return an offset for any sitedrive
        /// other than the primary then that sitedrive will not be included.
        /// Otherwise considers additional sitedrives from allSDs
        /// </summary>
        private ContextualMeshMessage MakeContextualMeshMessage(SiteDrive primarySD,
                                                                Dictionary<int, List<SiteDrive>> changedSDsBySol,
                                                                Dictionary<SiteDrive, SiteDriveList> allSDs,
                                                                PlacesDB placesDB = null)
        {
            SiteDriveList primarySDList = allSDs[primarySD];

            string rdrDir = primarySDList.RDRDir;
            int primarySol = primarySDList.MaxSol;

            string name = string.Format("{0}_{1}", SolToString(primarySol), primarySD.ToString());

            int minSol = Math.Max(0, primarySol - solRange);
            int maxSol = primarySol + solRange;
            primarySDList = primarySDList.FilterToSolRange(minSol, maxSol);

            //primary site drive must have at least this many wedges
            //unless it's the highest numbered sitedrive in its sol
            if ((!changedSDsBySol.ContainsKey(primarySol) || primarySD < changedSDsBySol[primarySol].Max()) &&
                options.MinPrimarySiteDriveWedges > 0 && primarySDList.NumWedges < options.MinPrimarySiteDriveWedges)
            {
                pipeline.LogInfo("skipping contextual mesh {0} in {1}, {2} < {3} wedges",
                                 name, rdrDir, primarySDList.NumWedges, options.MinPrimarySiteDriveWedges);
                return null;
            }

            int maxWedges = mission.GetContextualMeshMaxWedges();
            int maxTextures = mission.GetContextualMeshMaxTextures();
            double maxDistance = options.MaxSiteDriveDistance;

            pipeline.LogInfo("filtering sitedrives for contextual mesh{0}{1}" +
                             ", max wedges {2} ({3} navcam/sitedrive, {4} mastcam/sitedrive)" +
                             ", max textures {5} ({6} navcam/sitedrive, {7} mastcam/sitedrive)" +
                             ", prefer {8}",
                             maxDistance > 0 ? $", max distance {maxDistance}" : "",
                             maxSDs < int.MaxValue ? $", max sitedrives {maxSDs}" : "",
                             maxWedges,
                             mission.GetContextualMeshMaxNavcamWedgesPerSiteDrive(),
                             mission.GetContextualMeshMaxMastcamWedgesPerSiteDrive(),
                             maxTextures,
                             mission.GetContextualMeshMaxNavcamTexturesPerSiteDrive(),
                             mission.GetContextualMeshMaxMastcamTexturesPerSiteDrive(),
                             mission.GetContextualMeshPreferOlderProducts() ? "older" : "newer");

            var keepers = new Dictionary<SiteDrive, SiteDriveList>();
            keepers[primarySD] = primarySDList;
            var distance = new Dictionary<SiteDrive, double>();
            foreach (var sd in allSDs.Keys.OrderBy(sd => sd))
            {
                var list = allSDs[sd];

                if (list.RDRDir != rdrDir)
                {
                    pipeline.LogWarn("not including sitedrive {0} in contextual mesh for {1}, RDR dir {2} != {3}",
                                     sd, primarySD, list.RDRDir, rdrDir);
                    continue;
                }

                if (keepers.ContainsKey(sd))
                {
                    continue;
                }

                var filtered = list.ApplyMissionLimits().FilterToSolRange(minSol, maxSol);
                if (filtered.NumSols == 0)
                {
                    pipeline.LogInfo("not including sitedrive {0} in contextual mesh for {1}, " +
                                     "included sols {2} not in range {3}-{4}",
                                     sd, primarySD, MakeSolRanges(list.Sols), minSol, maxSol);
                    continue;
                }

                if (maxDistance <= 0)
                {
                    keepers[sd] = filtered;
                    continue;
                }

                if (placesDB == null)
                {
                    pipeline.LogWarn("not including sitedrive {0} in contextual mesh for {1}, " +
                                     "PlacesDB not available to check distance <= {2}", sd, primarySD, maxDistance);
                    continue;
                }

                try
                {
                    bool reinit = serviceMode && !options.MasterNoReinitPlacesDBPerQuery;
                    //if !reinit then the master loop already acquired longRunningCredentialRefreshLock
                    lock (reinit ? longRunningCredentialRefreshLock : new Object()) {
                        if (reinit)
                        {
                            placesDB = InitPlacesDB(quiet: true);
                        }
                        if (placesDB != null)
                        {
                            double dist = placesDB.GetOffset(primarySD, sd).Length();
                            if (dist <= maxDistance)
                            {
                                keepers[sd] = filtered;
                                distance[sd] = dist;
                            }
                            else
                            {
                                pipeline.LogInfo("not including sitedrive {0} in contextual mesh for {1}, " +
                                                 "PlacesDB distance {2:F3} > {3:F3}", sd, primarySD, dist, maxDistance);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    pipeline.LogException
                        (ex, $"not including sitedrive {sd} in contextual mesh for {primarySD}: " +
                         $"error checking distance <= {maxDistance} with PlacesDB");
                }
            }

            int totalSDs = keepers.Count;
            int totalWedges = keepers.Values.Sum(list => list.NumWedges);
            int totalTextures = keepers.Values.Sum(list => list.NumTextures);
            if (maxSDs < int.MaxValue || maxWedges < int.MaxValue || maxTextures < int.MaxValue)
            {
                var oversize = keepers.Values
                    .Where(l => l.SiteDrive != primarySD) //never cull the primary sitedrive
                    .Where(l => (l.NumWedges > maxWedges || l.NumTextures > maxTextures))
                    .Select(l => l.SiteDrive)
                    .ToList();

                foreach (var dead in oversize) {
                    pipeline.LogInfo("not including oversize sitedrive {0} in contextual mesh for {1}: {2}",
                                     dead, primarySD, 
                                     keepers[dead].NumWedges > maxWedges ?
                                     $"{keepers[dead].NumWedges} wedges > {maxWedges}" :
                                     $"{keepers[dead].NumTextures} textures > {maxTextures}");
                    totalSDs--;
                    totalWedges -= keepers[dead].NumWedges;
                    totalTextures -= keepers[dead].NumTextures;
                    keepers.Remove(dead);
                }

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

                //delete older sitedrives first
                prioritized = prioritized.OrderBy(sd => keepers[sd].MaxSol).ToList();

                var queue = new Queue<SiteDrive>(prioritized);
                while (queue.Count > 0 && (totalSDs > maxSDs || totalWedges > maxWedges || totalTextures > maxTextures))
                {
                    var dead = queue.Dequeue();
                    string distMsg = placesDB != null? $", distance {distance[dead]:F3}" : "";
                    string limitMsg = "";
                    if (totalSDs > maxSDs)
                    {
                        limitMsg = $"total sitedrives {totalSDs} <= {maxSDs}";
                    }
                    if (totalWedges > maxWedges)
                    {
                        limitMsg += (limitMsg != "" ? ", " : "") + $"total wedges {totalWedges} <= {maxWedges}";
                    }
                    if (totalTextures > maxTextures)
                    {
                        limitMsg += (limitMsg != "" ? ", " : "") + $"total textures {totalTextures} <= {maxTextures}";
                    }
                    pipeline.LogInfo("not including sitedrive {0} (sols {1}, {2} wedges, {3} textures{4}) " +
                                     "in contextual mesh for {5} to enforce {6}",
                                     dead, MakeSolRanges(keepers[dead].Sols), keepers[dead].NumWedges,
                                     keepers[dead].NumTextures, distMsg, primarySD, limitMsg);
                    totalSDs--;
                    totalWedges -= keepers[dead].NumWedges;
                    totalTextures -= keepers[dead].NumTextures;
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
                sols = MakeSolRanges(sols),
                siteDrives = string.Join(",", keepers.Keys.OrderBy(sd => sd)),
                numWedges = totalWedges,
                timestamp = (long)UTCTime.NowMS(),
                extent = !options.NoAllowOverrideExtent ? GetExtent(primarySol, primarySD) : options.Extent
            };
        }

        /// <summary>
        /// Combines a batch of new contextual mesh messages with existing ones in the worker queue.
        /// Also optionally removes messages for tilesets which are already processed (or being processed).
        /// The messages must all have the same RDR dir.
        /// De-dupes, preferring newer-created messages to older.
        /// enforces max sitedrives per sol, max message age, max receive count
        /// Returns messages sorted first by decreasing sol, then by decreasing number of wedges.
        /// </summary>
        private List<ContextualMeshMessage> CoalesceMessages(List<ContextualMeshMessage> newMsgsOldestToNewest,
                                                             string what, MessageQueue queue, string rdrDir,
                                                             int checkExistingSolRange, bool includeExistingMessages,
                                                             int maxSiteDrivesPerSol)
        {
            if (newMsgsOldestToNewest.Count == 0)
            {
                return newMsgsOldestToNewest;
            }

            if (checkExistingSolRange >= 0)
            {
                checkExistingSolRange = Math.Max(0, checkExistingSolRange);
                pipeline.LogInfo("checking {0} messages for already existing {1} tilesets in +-{2} sols",
                                 newMsgsOldestToNewest.Count, what, checkExistingSolRange);
                var keep = new List<ContextualMeshMessage>();
                foreach (var msg in newMsgsOldestToNewest)
                {
                    int minSol = Math.Max(0, msg.primarySol - checkExistingSolRange);
                    int maxSol = msg.primarySol + checkExistingSolRange;
                    string desc = DescribeMessage(msg, verbose: true);
                    pipeline.LogInfo("checking sol range [{0}-{1}] for tileset: {2}", minSol, maxSol, desc);
                    int foundSol = -1;
                    void check(int sol)
                    {
                        //only checking version 0 here
                        //this could return false negative if e.g. version 0 went zombie but a later version is done
                        //this might mean we recreate the tileset even though it exists in another sol
                        //but that's not the end of the world
                        string solDir = StringHelper.ReplaceIntWildcards(rdrDir, sol);
                        var status = CheckForTileset(msg, GetDestDir(solDir, quiet: true), 0, sol);
                        if ((status == TilesetStatus.done) || (status == TilesetStatus.processing))
                        {
                            foundSol = sol;
                        }
                    }
                    for (int sol = msg.primarySol; foundSol < 0 && sol >= minSol; sol--)
                    {
                        check(sol);
                    }
                    for (int sol = msg.primarySol + 1; foundSol < 0 && sol <= maxSol; sol++)
                    {
                        check(sol);
                    }
                    if (foundSol < 0)
                    {
                        keep.Add(msg);
                    }
                    else
                    {
                        pipeline.LogInfo("{0} mesh exists in sol {1}, dropping: {2}", what, foundSol, desc);
                    }
                }
                if (keep.Count == 0)
                {
                    return keep;
                }
                newMsgsOldestToNewest = keep;
            }

            pipeline.LogInfo("coalescing {0} new {1} messages{2}",
                             newMsgsOldestToNewest.Count, what, includeExistingMessages ? " with existing" : "");

            //keep only unique messages
            //this is where ContextualMeshMessage GetHashCode() and Equals() get used
            var keepers = new HashSet<ContextualMeshMessage>();
            void keepUniqueNewest(List<ContextualMeshMessage> msgs, string kind)
            {
                for (int i = msgs.Count - 1; i >= 0; i--) //iterate newest -> oldest
                {
                    if (!keepers.Contains(msgs[i]))
                    {
                        keepers.Add(msgs[i]);
                    }
                    else
                    {
                        pipeline.LogInfo("{0} {1} mesh message superceded by a newer one, dropping: {2}",
                                         kind, what, DescribeMessage(msgs[i], verbose: true));
                    }
                }
            }

            //it is possible, but unlikely, that there are dupes even in new messages
            keepUniqueNewest(newMsgsOldestToNewest, "new");

            //now reap all the existing messages in the worker queue for the same rdrDir
            //and keep any that aren't dupes of new messages
            //really there should be no dupes among the old messages
            //but just in case, keep them in order
            var oldMsgsOldestToNewest = new List<ContextualMeshMessage>();
            while (includeExistingMessages)
            {
                var msg = queue.DequeueOne<ContextualMeshMessage>() as ContextualMeshMessage;
                if (msg == null)
                {
                    break;
                }
                if (msg.rdrDir == rdrDir)
                {
                    queue.DeleteMessage(msg);
                    if (!options.DeprioritizeRetries && msg.ApproxReceiveCount > 1)
                    {
                        //with DeprioritizeRetries worker will recycle message and increment numFailedAttempts
                        //but otherwise we need to bookeep the receive count here
                        //(minus one because we just received it)
                        msg.numFailedAttempts += (msg.ApproxReceiveCount - 1);
                    }
                    oldMsgsOldestToNewest.Add(msg);
                }
            }
            int oldMsgsCount = oldMsgsOldestToNewest.Count;
            if (includeExistingMessages)
            {
                pipeline.LogInfo("dequeued {0} {1} messages from {2}", oldMsgsCount, what, queue.Name);
            }
            
            int maxAgeSec = GetMaxMessageAgeSec();
            int maxReceiveCount = GetMaxReceiveCount();
            double nowMS = 1e3 * UTCTime.Now();
            var oldKeepers = new List<ContextualMeshMessage>();
            foreach (var msg in oldMsgsOldestToNewest) {
                int ageSec = (int)(0.001 * (nowMS - GetFirstSendMS(msg)));
                string reason =
                    (ageSec > maxAgeSec) ?
                    string.Format("too old {0} > {1}", Fmt.HMS(ageSec * 1e3), Fmt.HMS(maxAgeSec * 1e3)) :
                    (msg.numFailedAttempts > maxReceiveCount) ?
                    string.Format("too many retries {0} > {1}", msg.numFailedAttempts, maxReceiveCount) :
                    null;
                if (string.IsNullOrEmpty(reason))
                {
                    oldKeepers.Add(msg);
                }
                else
                {
                    string desc = DescribeMessage(msg);
                    pipeline.LogError("{0} {1}, removing from queue, {2} fail queue", desc, reason,
                                      failMessageQueue != null ? "adding to" : "no");
                    if (failMessageQueue != null)
                    {
                        try
                        {
                            failMessageQueue.Enqueue(msg);
                        }
                        catch (Exception failQueueException)
                        {
                            pipeline.LogException(failQueueException, "adding message to fail queue");
                        }
                    }
                }
            }
            keepUniqueNewest(oldKeepers, "old");

            if (maxSiteDrivesPerSol > 0)
            {
                var msgsBySol = new Dictionary<int, List<ContextualMeshMessage>>();
                foreach (var msg in keepers)
                {
                    if (!msgsBySol.ContainsKey(msg.primarySol))
                    {
                        msgsBySol[msg.primarySol] = new List<ContextualMeshMessage>();
                    }
                    msgsBySol[msg.primarySol].Add(msg);
                }
                var sols = msgsBySol.Keys.ToList(); //avoid (InvalidOperationException) Collection was modified
                foreach (int sol in sols)
                {
                    var filtered = msgsBySol[sol]
                        .OrderByDescending(msg => msg.primarySiteDrive)
                        .Take(maxSiteDrivesPerSol)
                        .ToList();
                    if (filtered.Count < msgsBySol[sol].Count)
                    {
                        var discarded = msgsBySol[sol]
                            .OrderByDescending(msg => msg.primarySiteDrive)
                            .Skip(maxSiteDrivesPerSol)
                            .ToList();
                        pipeline.LogInfo("kept {0} messages for {1} highest numbered sitedrives {2} for sol {3}, " +
                                         "discarded {4} others for sitedrives {5}", what, filtered.Count,
                                         string.Join(",", filtered.Select(m => m.primarySiteDrive)), sol,
                                         discarded.Count, string.Join(",", discarded.Select(m => m.primarySiteDrive)));
                    }   
                    msgsBySol[sol] = filtered;
                }
                keepers.Clear();
                keepers.UnionWith(msgsBySol.Values.SelectMany(msgs => msgs));
            }

            pipeline.LogInfo("kept {0} {1} coalesced messages from {2} old and {3} new",
                             keepers.Count, what, oldMsgsCount, newMsgsOldestToNewest.Count);

            //yes, OrderByDescending() is stable
            //https://stackoverflow.com/questions/1209935/orderby-and-orderbydescending-are-stable
            var coalesced = keepers
                .OrderByDescending(msg => msg.extent) //larger extent -> process sooner (lowest priority)
                .OrderByDescending(msg => msg.numWedges) //more wedges -> process sooner
                .OrderByDescending(msg => msg.primarySiteDrive) //higher sitedrive -> process sooner
                .OrderByDescending(msg => msg.primarySol) //higher sol -> process sooner
                .OrderBy(msg => msg.numFailedAttempts) //more failed attempts -> process later (highest priority)
                .ToList();

            //note message ordering will only be precisely respected if an SQS FIFO queue is used
            
            return coalesced;
        }

        //uses SQS, called only while holding credentialRefreshLock
        private int EnqueueMessages(List<Stamped<ContextualMeshMessage>> msgs,
                                    List<Stamped<ContextualMeshMessage>> forceMsgs, string what, MessageQueue queue,
                                    string rdrDir, int checkExistingSolRange, bool includeExistingMessages,
                                    int maxSiteDrivesPerSol)
        {
            if (queue == null)
            {
                pipeline.LogError("cannot enqueue {0} {1} mesh messages, failed to open queue", msgs.Count, what);
                return 0;
            }

            //default order messages by when the corresponding sitedrive list changed (oldest to newest)
            var coalesced = msgs.OrderBy(sm => sm.Timestamp).Select(sm => sm.Value).ToList();
            try
            {
                //remove duplicates and order descending by sol, then sitedrive, then num wedges
                coalesced = CoalesceMessages(coalesced, what, queue, rdrDir, checkExistingSolRange,
                                             includeExistingMessages, maxSiteDrivesPerSol);
            }
            catch (Exception ex)
            {
                pipeline.LogException(ex, $"error coalescing {what} mesh messages, proceeding with un-coaleseced");
            }

            if (forceMsgs != null && forceMsgs.Count > 0)
            {
                coalesced.AddRange(forceMsgs.OrderBy(sm => sm.Timestamp)
                                   .Select(sm =>
                                   {
                                       var msg = sm.Value;
                                       msg.force = true;
                                       return msg;
                                   }));
                try
                {
                    coalesced = CoalesceMessages(coalesced, what, queue, rdrDir, -1, false, -1);
                }
                catch (Exception ex)
                {
                    pipeline.LogException(ex, $"error coalescing {what} mesh messages, proceeding with un-coaleseced");
                }
            }
            
            //TODO right about here we should try to determine if any workers
            //are processing meshes for which there are new messages and if so, ask them to abort
            
            pipeline.LogInfo("enqueueing {0} {1} mesh messages to {2} for {3}",
                             coalesced.Count, what, queue.Name, rdrDir);

            int n = 0;
            foreach (var msg in coalesced)
            {
                try
                {
                    pipeline.LogInfo("enqueueing {0} mesh message to {1}: {2}",
                                     what, queue.Name, DescribeMessage(msg, verbose: true));
                    queue.Enqueue(msg);
                    n++;
                }
                catch (Exception ex)
                {
                    pipeline.LogException(ex, $"adding {what} message to {queue.Name}");
                }
            }
            return n;
        }

        private MessageQueue GetWorkerMessageQueue()
        {
            return GetMessageQueue(options.WorkerQueueName, GetDefaultMessageTimeoutSec(),
                                   options.LandformOwnedWorkerQueue, "worker");
        }

        private MessageQueue GetOrbitalWorkerMessageQueue()
        {
            return GetMessageQueue(options.OrbitalWorkerQueueName, GetDefaultMessageTimeoutSec(),
                                   options.LandformOwnedWorkerQueue, "orbital worker");
        }

        private DateTime ToLocalTime(long timestamp)
        {
            return UTCTime.MSSinceEpochToDate(timestamp).ToLocalTime();
        }

        private bool UsePlacesDB()
        {
            var cfg = PlacesConfig.Instance;
            return mission.AllowPlacesDB() && !string.IsNullOrEmpty(cfg.Url) && !string.IsNullOrEmpty(cfg.View);
        }

        private PlacesDB InitPlacesDB(bool quiet = false)
        {
            if (UsePlacesDB())
            {
                try
                {
                    var placesDB = new PlacesDB(pipeline);
                    pipeline.LogInfo("using PlacesDB " + PlacesConfig.Instance.Url);
                    if (placesDBCacheMaxAgeSec > 0)
                    {
                        double now = UTCTime.Now();
                        double age = now - placesDBCacheTime;
                        if (placesDBCache != null && placesDBCacheTime >= 0 && age > placesDBCacheMaxAgeSec)
                        {
                            pipeline.LogInfo("clearing PlacesDB cache, age {0} > {1}",
                                             Fmt.HMS(1e3 * age), Fmt.HMS(1e3 * placesDBCacheMaxAgeSec));
                            placesDBCache = null;
                        }
                        if (placesDBCache != null)
                        {
                            if (!quiet)
                            {
                                pipeline.LogInfo("re-using existing PlacesDB cache, age {0} <= {1}",
                                                 Fmt.HMS(1e3 * age), Fmt.HMS(1e3 * placesDBCacheMaxAgeSec));
                            }
                            placesDB.SetCache(placesDBCache);
                        }
                        else
                        {
                            placesDBCache = placesDB.GetCache();
                            placesDBCacheTime = now;
                            pipeline.LogInfo("saving PlacesDB cache for later re-use");
                        }
                    }
                    else if (!quiet)
                    {
                        pipeline.LogInfo("not restoring PlacesDB cache, max age{0}s", placesDBCacheMaxAgeSec);
                    }
                    return placesDB;
                }
                catch (Exception ex)
                {
                    pipeline.LogError("error initializing PlacesDB: {0}", ex.Message);
                }
            }
            if (!quiet)
            {
                pipeline.LogInfo("not using PlacesDB");
            }
            return null;
        }

        //uses EC2, called only by MasterLoop() [-> WorkerAutoStart()] while holding credentialRefreshLock 
        private void StartWorkers(List<string> instances, string what = "")
        {
            if (instances != null && instances.Count > 0)
            {
                what = (!string.IsNullOrEmpty(what) ? (what + " ") : "") + "EC2 worker instances";
                pipeline.LogInfo("ensuring {0} are running: {1}", what, String.Join(", ", instances));
                if (instances.Count == 1 && instances[0].StartsWith("asg:"))
                {
                    string asg = instances[0].Substring(4);
                    try
                    {
                        int size = 1;
                        if (asg.Contains(":"))
                        {
                            string[] split = StringHelper.ParseList(asg, ':');
                            if (split.Length == 2)
                            {
                                asg = split[0];
                                size = int.Parse(split[1]); //throws exception if not an int
                            }
                            else
                            {
                                throw new Exception($"invalid format, expected asg:<name>:<size>, got \"asg:{asg}\"");
                            }
                        }
                        pipeline.LogInfo("setting auto scaling group {0} to {1} desired instances", asg, size);
                        if (!computeHelper.SetAutoScalingGroupSize(asg, size))
                        {
                            pipeline.LogError("failed to scale up {0}", asg);
                        }
                    }
                    catch (Exception ex)
                    {
                        pipeline.LogException(ex, $"creating auto scaling client or scaling up {asg}");
                    }
                    return;
                }
                var toStart = new List<string>();
                var pending = new List<string>();
                var running = new List<string>();
                foreach (var id in instances)
                {
                    try
                    {
                        switch (computeHelper.GetInstanceState(id))
                        {
                            case ComputeHelper.InstanceState.pending: pending.Add(id); break;
                            case ComputeHelper.InstanceState.running: running.Add(id); break;
                            default: toStart.Add(id); break;
                        }
                    }
                    catch (Exception ex)
                    {
                        pipeline.LogException(ex, "creating EC2 client or getting instance state");
                        toStart = instances;
                    }
                }
                if (pending.Count > 0)
                {
                    pipeline.LogInfo("{0} {1} already pending start: {2}",
                                     pending.Count, what, String.Join(", ", pending));
                }
                if (running.Count > 0)
                {
                    pipeline.LogInfo("{0} {1} already running: {2}",
                                     running.Count, what, String.Join(", ", running));
                }
                if (toStart.Count > 0)
                {
                    try
                    {
                        pipeline.LogInfo("requesting start of {0} {1}: {2}",
                                         toStart.Count, what, String.Join(", ", toStart));
                        if (!computeHelper.StartInstances(toStart.ToArray()))
                        {
                            pipeline.LogError("failed to start EC2 instances");
                        }
                    }
                    catch (Exception ex)
                    {
                        pipeline.LogException(ex, "creating EC2 client or starting instances");
                    }
                }
            }
        }

        //uses SQS and EC2, called only by MasterLoop() while holding credentialRefreshLock
        private void WorkerAutoStart(string what, MessageQueue queue, List<string> instances)
        {
            try
            {
                if (instances != null && queue != null && queue.GetNumMessages() > 0)
                {
                    StartWorkers(instances);
                }
            }
            catch (Exception ex)
            {
                pipeline.LogException(ex, $"in {what} auto start");
            }
        }

        private void EnqueueOrbitalMessages(DictionaryOfChangedURLs urls,
                                            List<Stamped<SolutionNotificationMessage>> solutionMsgs = null)
        {
            var rdrDirs = new HashSet<string>(urls.Keys);
            var forceMsgs = new List<Stamped<ContextualMeshMessage>>();
            if (solutionMsgs != null)
            {
                foreach (var msg in solutionMsgs)
                {
                    var snm = msg.Value;
                    try
                    {
                        if (AssignSolAndRDRDir(snm))
                        {
                            var omm = MakeOrbitalMeshMessage(snm.rdrDir, snm.sol, snm.GetSiteDrive());
                            pipeline.LogInfo("created orbital mesh message for {0}: {1}",
                                             snm, DescribeMessage(omm, verbose: true));
                            forceMsgs.Add(new Stamped<ContextualMeshMessage>(omm, msg.Timestamp));
                            rdrDirs.Add(snm.rdrDir);
                        }
                        else
                        {
                            pipeline.LogWarn("failed to assign sol and/or RDR dir for {0}, " +
                                             "not creating orbital mesh message", snm);
                        }
                    } 
                    catch (Exception ex)
                    {
                        pipeline.LogException(ex, "error assigning sol and RDR dir for " + snm +
                                              ", not creating orbital mesh message");
                    }
                }
            }
            foreach (var rdrDir in rdrDirs)
            {
                pipeline.LogInfo("processing RDR directory {0} for new orbital meshes", rdrDir);

                var omsgs = new List<Stamped<ContextualMeshMessage>>();
                if (urls.ContainsKey(rdrDir))
                {
                    foreach (var sd in urls[rdrDir].Keys.OrderByDescending(sd => sd).ToList())
                    {
                        if (urls[rdrDir][sd].Count > 0)
                        {
                            int sol = urls[rdrDir][sd]
                                .Max(entry =>
                                     SiteDriveList.GetSol(entry.Key, RoverProductId.Parse(entry.Key, mission,
                                                                                          throwOnFail: false)));
                            if (sol >= 0)
                            {
                                long stamp = urls[rdrDir][sd].Max(entry => entry.Value);
                                var msg = MakeOrbitalMeshMessage(rdrDir, sol, sd);
                                pipeline.LogInfo("created orbital mesh message for changed sitedrive {0}: {1}",
                                                 sd, DescribeMessage(msg, verbose: true));
                                omsgs.Add(new Stamped<ContextualMeshMessage>(msg, stamp));
                            }
                            else
                            {
                                pipeline.LogWarn("error getting sol for orbital mesh {0} in {1}, first url {2}",
                                                 sd, rdrDir, urls[rdrDir][sd].First().Key);
                            }
                        }
                    }
                }
                lock ((options.NoUseDefaultAWSProfileForEC2Client || options.NoUseDefaultAWSProfileForSQSClient) ?
                      credentialRefreshLock : new Object())
                {
                    var queue = orbitalWorkerQueue ?? workerQueue;
                    bool cullExisting = !options.RecreateExistingOrbital;
                    int checkExistingSolRange = cullExisting ? options.OrbitalCheckExistingSolRange : -1;
                    var force = forceMsgs.Where(m => m.Value.rdrDir == rdrDir).ToList();
                    pipeline.LogInfo("{0} orbital mesh messages from PLACES solution notifications", force.Count);
                    pipeline.LogInfo("{0} orbital mesh messages from changed sitedrives", omsgs.Count);
                    int numEnqueued = EnqueueMessages(omsgs, force, "orbital", queue, rdrDir, checkExistingSolRange,
                                                      options.CoalesceExistingOrbitalMessages,
                                                      options.MaxOrbitalSiteDrivesPerSol);
                    if (numEnqueued > 0 && options.AutoStartWorkers && options.WorkerAutoStartSec > 0)
                    {
                        StartWorkers(orbitalWorkerInstances, "orbital");
                    }
                }
            }
        }

        private void EnqueueContextualMessages(DictionaryOfChangedURLs urls,
                                               List<Stamped<SolutionNotificationMessage>> solutionMsgs = null)
        {
            var rdrDirs = new HashSet<string>(urls.Keys);
            if (solutionMsgs != null)
            {
                var tmp = new List<Stamped<SolutionNotificationMessage>>();
                foreach (var msg in solutionMsgs)
                {
                    var snm = msg.Value;
                    try
                    {
                        //contextualPlacesSolutionNotifications and orbitalPlacesSolutionNotifications contain
                        //references to the same underlying set of SolutionNotificationMessage objects
                        //and AssignSolAndRDRDir() will early out if it's already been run on msg
                        if (AssignSolAndRDRDir(snm))
                        {
                            tmp.Add(msg);
                            rdrDirs.Add(snm.rdrDir);
                        }
                        else
                        {
                            pipeline.LogWarn("failed to assign sol and/or RDR dir for {0}, " +
                                             "not creating contextual mesh message", snm);
                        }
                    } 
                    catch (Exception ex)
                    {
                        pipeline.LogException(ex, "error assigning sol and RDR dir for " + snm +
                                              ", not creating contextual mesh message");
                    }
                }
                solutionMsgs = tmp;
            }
            else
            {
                solutionMsgs = new List<Stamped<SolutionNotificationMessage>>();
            }
            foreach (var rdrDir in rdrDirs)
            {
                pipeline.LogInfo("processing RDR directory {0} for new contextual meshes", rdrDir);

                Dictionary<SiteDrive, Stamped<SiteDriveList>> sdLists = null;
                List<SiteDrive> changedSDs = null;
                Dictionary<SiteDrive, SiteDriveList> allSDs = null;
                Dictionary<int, List<SiteDrive>> changedSDsBySol = null;
                if (urls.ContainsKey(rdrDir))
                {
                    lock (options.NoUseDefaultAWSProfileForS3Client ? longRunningCredentialRefreshLock : new Object())
                    {
                        sdLists = LoadSiteDriveLists(rdrDir, urls[rdrDir]);
                    }
                    
                    changedSDs = urls[rdrDir].Keys
                        .Where(sd => sdLists.ContainsKey(sd))
                        .OrderByDescending(sd => sd)
                        .ToList();
                    
                    if (changedSDs.Count > 0)
                    {
                        allSDs = new Dictionary<SiteDrive, SiteDriveList>();
                        foreach (var entry in sdLists)
                        {
                            allSDs[entry.Key] = entry.Value.Value;
                        }
                        
                        changedSDsBySol = new Dictionary<int, List<SiteDrive>>();
                        foreach (var sd in changedSDs)
                        {
                            int sol = allSDs[sd].MaxSol;
                            if (!changedSDsBySol.ContainsKey(sol))
                            {
                                changedSDsBySol[sol] = new List<SiteDrive>();
                            }
                            changedSDsBySol[sol].Add(sd);
                        }
                    }
                    else
                    {
                        pipeline.LogWarn("failed to load sitedrive lists for RDR dir {0} " +
                                         "with {1} changed URLs in {2} changed sitedrives {3}",
                                         rdrDir, urls[rdrDir].Values.Sum(u => u.Count),
                                         urls[rdrDir].Count, string.Join(",", urls[rdrDir].Keys));
                    }
                }
                
                //try to connect to PlacesDB just for this pass
                //rather than having a single long-lived PlacesDB connection
                //for one thing our PlacesDB interface caches results, and the cache could become stale
                //also, particularly in certain dev scenarios, PlacesDB availability may be iffy
                //better to try on each pass rather than once ever
                var msgs = new List<Stamped<ContextualMeshMessage>>();
                var forceMsgs = new List<Stamped<ContextualMeshMessage>>();
                bool usePlaces = UsePlacesDB();
                bool reinitPlaces = !options.MasterNoReinitPlacesDBPerQuery;
                lock (usePlaces && !reinitPlaces ? longRunningCredentialRefreshLock : new Object())
                {
                    var placesDB = usePlaces ? InitPlacesDB() : null;

                    if (sdLists != null && changedSDs != null && allSDs != null && changedSDsBySol != null)
                    {
                        foreach (var changedSD in changedSDs)
                        {
                            try
                            {
                                var msg = MakeContextualMeshMessage(changedSD, changedSDsBySol, allSDs, placesDB);
                                if (msg != null)
                                {
                                    pipeline.LogInfo("created contextual mesh message for changed sitedrive {0}: {1}",
                                                     changedSD, DescribeMessage(msg, verbose: true));
                                    msgs.Add(new Stamped<ContextualMeshMessage>(msg, sdLists[changedSD].Timestamp));
                                }
                                else
                                {
                                    pipeline.LogWarn(
                                        "failed to create contextual mesh message for changed sitedrive {0}",
                                        changedSD);
                                }
                            }
                            catch (Exception ex)
                            {
                                pipeline.LogException(ex, "error processing sitedrive " + changedSD);
                            }
                        }
                    }

                    foreach (var msg in solutionMsgs.Where(msg => msg.Value.rdrDir == rdrDir))
                    {
                        var snm = msg.Value;
                        try
                        {
                            var sd = snm.GetSiteDrive();
                            if (options.RebuildContextualAtPreviousEndOfDriveOnPLACESNotification)
                            {
                                try
                                {
                                    var psd = placesDB.GetPreviousEndOfDrive(sd, snm.View);
                                    var pmm = MakeContextualMeshMessage(snm.rdrDir, snm.sol, psd);
                                    if (pmm != null)
                                    {
                                        pipeline.LogInfo(
                                            "creating contextual mesh message for {0} at previous end-of-drive: {1}",
                                            snm, DescribeMessage(pmm, verbose: true));
                                        forceMsgs.Add(new Stamped<ContextualMeshMessage>(pmm, msg.Timestamp));
                                    }
                                    else
                                    {
                                        pipeline.LogWarn("failed to create contextual mesh message for {0} " +
                                                         "at previous end-of-drive {1}", snm, psd);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    pipeline.LogException(ex, "error creating contextual mesh message " +
                                                          "at previous end-of-drive for " + snm);
                                }
                            }
                            if (!options.OnlyRebuildContextualAtPreviousEndOfDriveOnPLACESNotification)
                            {
                                var cmm = MakeContextualMeshMessage(snm.rdrDir, snm.sol, sd);
                                if (cmm != null)
                                {
                                    pipeline.LogInfo("creating contextual mesh message for {0}: {1}",
                                                     snm, DescribeMessage(cmm, verbose: true));
                                    forceMsgs.Add(new Stamped<ContextualMeshMessage>(cmm, msg.Timestamp));
                                }
                                else
                                {
                                    pipeline.LogWarn("failed to create contextual mesh message for {0} ", snm);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            pipeline.LogException(ex, "error processing " + snm);
                        }
                    }
                }

                lock ((options.NoUseDefaultAWSProfileForEC2Client || options.NoUseDefaultAWSProfileForSQSClient) ?
                      credentialRefreshLock : new Object())
                {
                    bool cullExisting = options.NoRecreateExistingContextual;
                    int checkExistingSolRange = cullExisting ? 0 : -1;
                    pipeline.LogInfo("{0} contextual mesh messages from PLACES solution notifications",
                                     forceMsgs.Count);
                    pipeline.LogInfo("{0} contextual mesh messages from changed sitedrives", msgs.Count);
                    int numEnqueued = EnqueueMessages(msgs, forceMsgs, "contextual", workerQueue, rdrDir,
                                                      checkExistingSolRange, options.CoalesceExistingContextualMessages,
                                                      options.MaxContextualSiteDrivesPerSol);
                    if (numEnqueued > 0 && options.AutoStartWorkers && options.WorkerAutoStartSec > 0)
                    {
                        StartWorkers(workerInstances);
                    }
                }
            }

            pipeline.DeleteDownloadCache();
            FetchData.ExpireEDRCache(msg => pipeline.LogInfo(msg));
        }

        //changedURLs: RDR directory -> sitedrive -> list or wedge URL -> last changed UTC milliseconds
        DictionaryOfChangedURLs ProcessChangedURLs(DictionaryOfChangedURLs changedURLs, bool eop, string eopMsg,
                                                   string what)
        {
            long now = (long)UTCTime.NowMS();
            var urls = new DictionaryOfChangedURLs();
            lock (changedURLs)
            {
                foreach (string rdrDir in changedURLs.Keys)
                {
                    long lastChange = changedURLs[rdrDir].Values
                        .SelectMany(d => d.Values)
                        .DefaultIfEmpty(0)
                        .Max();

                    bool debounceTimeout = lastChange > 0 && (debounceMS <= 0 || lastChange <= (now - debounceMS));

                    if (eop || debounceTimeout)
                    {
                        urls[rdrDir] = changedURLs[rdrDir];
                        string lastChangeMsg = lastChange >= 0 ?
                            $", last change at {ToLocalTime(lastChange)}, {(debounceMS / 1000):f3}s debounce" : "";
                        pipeline.LogInfo("processing RDR dir {0} for new {1} meshes, {2} changed sitedrives" +
                                         "{3}, {4}", rdrDir, what, urls[rdrDir].Count,
                                         lastChangeMsg, eop ? eopMsg : "(RDR debounce expired)");
                    }
                }
                foreach (string rdrDir in urls.Keys)
                {
                    changedURLs.Remove(rdrDir);
                }
            }
            return urls;
        }
                    
        private void MasterLoop()
        {
            double lastStartSec = -1;
            int targetPeriodSec = MASTER_LOOP_PERIOD_SEC;
            pipeline.LogInfo("running master loop, period {0}s, debounce {1}s", targetPeriodSec, debounceMS / 1000);

            long latentEOP = 0;
            string latentEOPMessage = "";

            while (!abort)
            {
                if (lastStartSec >= 0)
                {
                    double actualPeriodSec = UTCTime.Now() - lastStartSec;
                    SleepSec(targetPeriodSec - actualPeriodSec); //negative ignored
                }
                lastStartSec = UTCTime.Now();

                try
                {
                    if (options.AutoStartWorkers && options.WorkerAutoStartSec > 0 && lastWorkerAutoStartSec <= 0 ||
                        (UTCTime.Now() - lastWorkerAutoStartSec) >= options.WorkerAutoStartSec)
                    {
                        lastWorkerAutoStartSec = UTCTime.Now();

                        lock ((options.NoUseDefaultAWSProfileForEC2Client ||
                               options.NoUseDefaultAWSProfileForSQSClient) ?
                              credentialRefreshLock : new Object())
                        {
                            if (workerQueue != null)
                            {
                                WorkerAutoStart("contextual", workerQueue, workerInstances);
                            }
                            if (orbitalWorkerQueue != null)
                            {
                                WorkerAutoStart("orbital", orbitalWorkerQueue, orbitalWorkerInstances);
                            }
                        }
                    }

                    long eofMS = Interlocked.Exchange(ref eofTimestamp, 0); 
                    long eoxMS = Interlocked.Exchange(ref eoxTimestamp, 0); 
                    long eopMS = Interlocked.Exchange(ref eopTimestamp, 0); 
                    bool gotEOF = eofMS != 0;
                    bool gotEOX = eoxMS != 0;
                    bool gotEOP = eopMS != 0;
                    bool finalEOF = eofMS < 0;
                    bool finalEOX = eoxMS < 0;
                    bool finalEOP = eopMS < 0;
                    eofMS = Math.Abs(eofMS);
                    eoxMS = Math.Abs(eoxMS);
                    eopMS = Math.Abs(eopMS);

                    if (!options.NoOrbital)
                    {
                        //don't attempt to debounce EOF/EOX/EOP here
                        //that would add latency which is undesirable for orbital tilesets
                        //just let possibly redundant orbital tilesets get triggered here
                        //we will dedupe orbital tileset messages anyway including checking for already built products
                        var orbitalURLs = ProcessChangedURLs(changedOrbitalURLs, gotEOF || gotEOX || gotEOP,
                                                             gotEOF ? $"EOF at {ToLocalTime(eofMS)}" :
                                                             gotEOX ? $"EOX at {ToLocalTime(eoxMS)}" :
                                                             gotEOP ? $"EOP at {ToLocalTime(eopMS)}" : "",
                                                             "orbital");

                        var solutionMsgs = new List<Stamped<SolutionNotificationMessage>>();
                        lock (orbitalPlacesSolutionNotifications)
                        {
                            solutionMsgs.AddRange(orbitalPlacesSolutionNotifications);
                            orbitalPlacesSolutionNotifications.Clear();
                        }

                        if (orbitalURLs.Count > 0 || solutionMsgs.Count > 0)
                        {
                            EnqueueOrbitalMessages(orbitalURLs, solutionMsgs);
                        }
                    }

                    if (!options.NoSurface)
                    {
                        long eop = 0;
                        string eopMsg = "";
                        bool fin = false;
                        if (eoxMS > eop)
                        {
                            eop = eoxMS;
                            fin = finalEOX;
                            eopMsg = string.Format("EOX{0} at {1}", fin ? " (final)" : "" , ToLocalTime(eoxMS));
                        }
                        if (eopMS > eop)
                        {
                            eop = eopMS;
                            fin = finalEOP;
                            eopMsg = string.Format("EOP{0} at {1}", fin ? " (final)" : "" , ToLocalTime(eopMS));
                        }
                        bool wasLatent = false;
                        if (latentEOP > 0)
                        {
                            long lastChange = changedContextualURLs.Values
                                .SelectMany(d => d.Values)
                                .SelectMany(d => d.Values)
                                .DefaultIfEmpty(0).Max();
                            if (latentEOP < lastChange)
                            {
                                pipeline.LogInfo("dropping latent {0}: {1} < last change {2}",
                                                 latentEOPMessage, ToLocalTime(latentEOP), ToLocalTime(lastChange));
                            }
                            else if (eop > 0)
                            {
                                pipeline.LogInfo("dropping latent {0}, newer {1}", latentEOPMessage, eopMsg);
                            }
                            else
                            {
                                pipeline.LogVerbose("restoring latent {0} from {1}", latentEOPMessage,
                                                    ToLocalTime(latentEOP));
                                eop = latentEOP;
                                eopMsg = latentEOPMessage;
                                wasLatent = true;
                            }
                            latentEOP = 0;
                            latentEOPMessage = "";
                        }
                        if (!fin && eopDebounceMS > 0)
                        {
                            long now = (long)(UTCTime.NowMS());
                            if (now - eop < eopDebounceMS)
                            {
                                pipeline.LogVerbose("saving latent {0} from {1} at {2} < {3}", eopMsg, ToLocalTime(eop),
                                                    ToLocalTime(now), ToLocalTime(eop + eopDebounceMS));
                                latentEOP = eop;
                                latentEOPMessage = eopMsg;
                                eop = 0;
                                eopMsg = "";
                            }
                            else if (!string.IsNullOrEmpty(eopMsg))
                            {
                                pipeline.LogInfo("{0} debounce expired for {1}{2} {3} >= {4}", Fmt.HMS(eopDebounceMS),
                                                 wasLatent ? "latent " : "", eopMsg, ToLocalTime(now),
                                                 ToLocalTime(eop + eopDebounceMS));
                                eopMsg += $" ({(debounceMS / 1000):f3}s debounce expired)";
                            }
                        }

                        var contextualURLs = ProcessChangedURLs(changedContextualURLs, eop > 0, eopMsg, "contextual");

                        var solutionMsgs = new List<Stamped<SolutionNotificationMessage>>();
                        lock (contextualPlacesSolutionNotifications)
                        {
                            solutionMsgs.AddRange(contextualPlacesSolutionNotifications);
                            contextualPlacesSolutionNotifications.Clear();
                        }

                        if (contextualURLs.Count > 0 || solutionMsgs.Count > 0)
                        {
                            lock (contextualPassQueue)
                            {
                                if (contextualPassQueue.Count < MAX_CONTEXTUAL_PASS_QUEUE_SIZE)
                                {
                                    //don't delay orbital meshes while we collect adjacent sitedrives for contextual
                                    //in practice that can take like ~30min
                                    //https://github.jpl.nasa.gov/OnSight/Landform/issues/1224
                                    contextualPassQueue.Enqueue(new ContextualPass(contextualURLs, solutionMsgs));
                                    pipeline.LogInfo("enqueued pass for contextual processing, queue size {0}",
                                                 contextualPassQueue.Count);
                                }
                                else
                                {
                                    pipeline.LogError("failed to enqueue pass for contextual processing, " +
                                                      "queue size {0} >= {1}",
                                                      contextualPassQueue.Count, MAX_CONTEXTUAL_PASS_QUEUE_SIZE);
                                }
                            }
                        }
                    }
                }
                catch (Exception masterException)
                {
                    pipeline.LogException(masterException, string.Format("error in master loop, throttling {0}",
                                                                         Fmt.HMS(SERVICE_LOOP_THROTTLE_SEC * 1e3)));
                    SleepSec(SERVICE_LOOP_THROTTLE_SEC);
                }
            }
            pipeline.LogInfo("shutting down, exiting master loop");
        }

        private void ContextualPassLoop()
        {
            double lastStartSec = -1;
            int targetPeriodSec = MASTER_LOOP_PERIOD_SEC;
            pipeline.LogInfo("running contextual pass loop, period {0}s", targetPeriodSec);
            while (!abort)
            {
                if (lastStartSec >= 0)
                {
                    double actualPeriodSec = UTCTime.Now() - lastStartSec;
                    SleepSec(targetPeriodSec - actualPeriodSec); //negative ignored
                }
                lastStartSec = UTCTime.Now();

                try
                {
                    ContextualPass pass = null;
                    lock (contextualPassQueue)
                    {
                        if (contextualPassQueue.Count > 0)
                        {
                            pass = contextualPassQueue.Dequeue();
                            pipeline.LogInfo("dequeued pass for contextual processing, queue size {0}",
                                             contextualPassQueue.Count);
                        }
                    }
                    if (pass != null)
                    {
                        EnqueueContextualMessages(pass.urls, pass.solutionMsgs);
                    }
                }
                catch (Exception ex)
                {
                    pipeline.LogException(ex, string.Format("error in contextual pass loop, throttling {0}",
                                                            Fmt.HMS(SERVICE_LOOP_THROTTLE_SEC * 1e3)));
                    SleepSec(SERVICE_LOOP_THROTTLE_SEC);
                }
            }
            pipeline.LogInfo("shutting down, exiting contextual pass loop");
        }
    }
}

