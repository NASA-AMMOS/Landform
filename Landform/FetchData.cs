using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Threading;
using Microsoft.Xna.Framework;
using CommandLine;
using log4net;
using OPS.Util;
using OPS.Cloud;
using OPS.Imaging;
using OPS.Geometry;
using OPS.Pipeline;

namespace OPS.Landform
{
    /// "Stop Trying to Make Fetch Happen" - Regina George (Mean Girls)
    [Verb("fetch", HelpText = "Download data products from S3")]
    public class FetchDataOptions
    {
        [Value(0, Required = true, Default = null, HelpText = "'27-32' or '607,609' or a mixture '27-32,607,609-611'")]
        public string Sols { get; set; }

        [Value(1, Required = true, Default = null, HelpText = "output directory, e.g. c:/Users/$USERNAME/Downloads")]
        public string OutputDir { get; set; }
        
        [Value(2, Required = false, HelpText = "RDR search locations with sol replaced with ##### (ie s3://m20-roastt-staging/ocs/test/sol/#####/ids/rdr/")]
        public IEnumerable<string> SearchLocations { get; set; } = null;

        [Option(Required = false, Default = null, HelpText = "A set of comma delimited site drives to filter by `0000100000,0003101330`")]
        public string SiteDrives { get; set; }

        [Option(Required = false, Default = null, HelpText = "")]
        public string Include { get; set; }

        [Option(Required = true, HelpText = "")]
        public string InputAWSProfile { get; set; }

        [Option(Required = false, Default = "us-gov-west-1", HelpText = "")]
        public string InputAWSRegion { get; set; }
       
        [Option(Required = false, Default = -1, HelpText = "Control the number of concurrent downloads")]
        public int ConcurrentDownloads { get; set; }

        [Option(Required = false, Default = false, HelpText = "Overwrite existing files")]
        public bool Overwrite { get; set; }

        [Option(HelpText = "Mission flag enables mission specific behavior", Default = Mission.M2020)]
        public Mission Mission { get; set; }

        [Option(HelpText = "Disable filtering by mission-specific filename cretieria", Default = false)]
        public bool DisableMissionSpecificFilenameFilter { get; set; }
    }

    public class FetchData
    {
        private FetchDataOptions options;
        private MissionSpecific mission;

        private static readonly ILog logger = LogManager.GetLogger(typeof(FetchDataOptions));

        string[] extensions = new string[] { ".OBJ", ".IMG", ".PNG", ".MTL" };
        
        string[] defaultSearchLocations = new string[]
        {
            "s3://red-product/ods/surface/sol/#####/soas/rdr",
            "s3://red-product/proj/msl/redops/ods/surface/sol/#####/opgs/rdr",
            "s3://m20-roastt-staging/ocs/test/sol/#####/ids/rdr"
        };
        
        public FetchData(FetchDataOptions opts)
        {
            options = opts;
            if(options.SearchLocations == null || options.SearchLocations.Count() == 0)
            {
                options.SearchLocations = defaultSearchLocations;
            }
            mission = MissionSpecific.GetInstance(options.Mission);
        }

        string LocalPath(string s3Location)
        {
            string outputDir = Path.Combine(options.OutputDir, Path.GetDirectoryName(s3Location.Replace("s3://", "")));
            string localPath = PathHelper.ChangeDirectory(s3Location, outputDir);
            return localPath;
        }

        void DownloadFile(string s3Location)
        {
            var localPath = LocalPath(s3Location);    
            PathHelper.EnsureExists(Path.GetDirectoryName(localPath));
            TemporaryFile.GetAndMove(localPath, f =>
            {
                bool success = false;
                int retryCounter = 0;
                while (!success && retryCounter < 3)
                {
                    if (retryCounter > 0)
                    {
                        logger.Info("\tRetrying: " + Path.GetFileName(s3Location));
                    }
                    retryCounter++;
                    try
                    {
                        var inputStorageHelper = new StorageHelper(options.InputAWSProfile, options.InputAWSRegion);
                        success = inputStorageHelper.DownloadFile(s3Location, f);
                    }
                    catch (Exception e)
                    {
                        logger.Info("\tError downloading: " + Path.GetFileName(s3Location));
                        logger.Info("\t" + e.Message);
                    }
                    if (!success)
                    {
                        logger.Info("\tError downloading: " + Path.GetFileName(s3Location));
                    }
                }
            });

        }

        public IEnumerable<string> IndexFiles(string searchDir)
        {
            try
            {
                List<string> results = new List<string>();
                logger.Info("Searching " + searchDir);
                var inputStorageHelper = new StorageHelper(options.InputAWSProfile, options.InputAWSRegion);
                // TODO: Limit folder depth as "tiles" directory can result in long indexing time
                var paths = inputStorageHelper.SearchObjects(searchDir).ToList();
                foreach (var path in paths)
                {
                    results.Add(path);
                }
                return results;
            }
            catch (Amazon.S3.AmazonS3Exception e)
            {
                logger.Info("\tError scanning: " + searchDir);
                logger.Info("\t" + e.Message);
                return new string[] { };
            }
        }

        string[] ExpandSolSpecifier(string solString)
        {
            string[] parts = solString.Split(',');
            List<int> sols = new List<int>();
            foreach(var part in parts)
            {
                if(part.Contains('-'))
                {
                    var subparts = part.Split('-');
                    int startSol = int.Parse(subparts[0]);
                    int endSol = int.Parse(subparts[1]);
                    for(int i = startSol; i <= endSol; i++)
                    {
                        sols.Add(i);
                    }
                }
                else
                {
                    sols.Add(int.Parse(part));
                }                       
            }
            return sols.Distinct().OrderBy(x => x).Select(x=> x.ToString("00000")).ToArray();
        }

        List<string> Filter(List<string> products)
        {
            List<string> result = new List<string>();
            var acceptedSiteDrives = GetSiteDriveFilters();
            var acceptedProductIds = ProductIDFilter();
            foreach (var p in products)
            {
                string filename = Path.GetFileNameWithoutExtension(p);
                string ext = Path.GetExtension(p).ToUpper();
                var id = RoverProductId.ParseFromString(filename);
                string sd =
                    id != null && id.Producer == RoverProductProducer.OPGS ?
                    ((OPGSProductId)id).SiteDrive.ToString() : null;
                bool sdOkay = acceptedSiteDrives == null || acceptedSiteDrives.Contains(sd);
                bool pidOkay = acceptedProductIds == null || acceptedProductIds.Contains(filename);
                if (extensions.Contains(ext) &&
                   (options.DisableMissionSpecificFilenameFilter || mission.CheckFilename(filename))
                    && sdOkay && pidOkay)
                {
                    result.Add(p);
                }
            }
            return result;
        }

        public bool ShouldDownload(string s3Location)
        {
            if (options.Overwrite == true)
            {
                return true;
            }
            var inputStorageHelper = new StorageHelper(options.InputAWSProfile, options.InputAWSRegion);
            var localPath = LocalPath(s3Location);
            return !inputStorageHelper.FileSizeMatches(s3Location, localPath);
        }

        HashSet<string> ProductIDFilter()
        {
            if(options.Include == null)
            {
                return null;
            }
            return new HashSet<string>(File.ReadAllLines(options.Include).Where(s => !string.IsNullOrEmpty(s.Trim())).Select(s => Path.GetFileNameWithoutExtension(s)));
        }

        HashSet<string> GetSiteDriveFilters()
        {
            if(options.SiteDrives == null)
            {
                return null;
            }
            var results = new HashSet<string>();
            foreach (var v in options.SiteDrives.Trim().Split(','))
            {
                try
                {
                    new SiteDrive(v);
                    results.Add(v);
                }
                catch (ArgumentException)
                {
                    logger.Error("Invalid site drive argument");
                    throw;
                }
            }
            return results;
        }

        public int Run()
        {

            var sols = ExpandSolSpecifier(options.Sols);
            logger.Info("Seaching sols: " + string.Join(",", sols));

            ConcurrentDictionary<string, List<string>> solToProducts = new ConcurrentDictionary<string, List<string>>();
            CoreLimitedParallel.ForEach(sols, sol =>
            {
                var prods = new List<string>();
                foreach (var location in options.SearchLocations)
                {
                    var solLocation = location.Replace("#####", sol);
                    prods.AddRange(IndexFiles(solLocation));
                }
                solToProducts.TryAdd(sol, prods);
            });
            logger.Info("Filtering Files");
            foreach (var sol in sols)
            {
                solToProducts[sol] = Filter(solToProducts[sol]);
            }
            var po = new ParallelOptions() { MaxDegreeOfParallelism = options.ConcurrentDownloads };
            var totalFilesToDownload = solToProducts.SelectMany(s => s.Value);
            var remainingFilesToDownload = totalFilesToDownload;
            if(!options.Overwrite)
            {
                ConcurrentBag<string> toDownload = new ConcurrentBag<string>();
                CoreLimitedParallel.ForEach(totalFilesToDownload, po, f =>
                {
                    if(ShouldDownload(f))
                    {
                        toDownload.Add(f);
                    }
                });
                remainingFilesToDownload = toDownload.ToList();
            }
            logger.Info("Found " + (totalFilesToDownload.Count() - remainingFilesToDownload.Count()) + " on disk");
            logger.Info("Downloading " + remainingFilesToDownload.Count() + " files");
            int downloaded = 0;
            int total = remainingFilesToDownload.Count();
            CoreLimitedParallel.ForEach(remainingFilesToDownload, po, f =>
            {
                DownloadFile(f);
                Interlocked.Increment(ref downloaded);
                logger.Info("Downloaded: " + Path.GetFileName(f) + " ("+((downloaded*100)/total)+"%)");
            });
            return 0;
        }
    }
}
