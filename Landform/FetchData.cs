using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Net;
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
        [Value(0, Required = true, Default = null, HelpText = "sol numbers to download, e.g. '27-32', '607,609', '27-32,607,609-611'; or a comma-separated list of raw s3 or http URLs if --raw is also specified")]
        public string Input { get; set; }

        [Value(1, Required = true, Default = null, HelpText = "output directory, e.g. c:/Users/$USERNAME/Downloads")]
        public string OutputDir { get; set; }
        
        [Value(2, Required = false, HelpText = "RDR search locations with sol replaced with ##### (ie s3://m20-roastt-staging/ocs/test/sol/#####/ids/rdr/")]
        public IEnumerable<string> SearchLocations { get; set; } = null;

        [Option(Required = false, Default = false, HelpText = "Treat input as raw S3 URLs, not sol numbers")]
        public bool Raw { get; set; }

        [Option(Required = false, Default = false, HelpText = "Suppress subdirs in output")]
        public bool NoSubdirs { get; set; }

        [Option(Required = false, Default = null, HelpText = "A set of comma delimited site drives to filter by, e.g. '0000100000,0003101330', wildcard 'xxxxx'")]
        public string SiteDrives { get; set; }

        [Option(Required = false, Default = null, HelpText = "Text file listing filenames without extension (product IDs) to include, one per line")]
        public string Include { get; set; }

        [Option(Required = false, Default = false, HelpText = "Download PNG products")]
        public bool WithPNG { get; set; }

        [Option(Required = false, Default = false, HelpText = "Download OBJ products, implies --withpng")]
        public bool WithOBJ { get; set; }

        [Option(Required = false, Default = false, HelpText = "Don't download PDS products")]
        public bool NoPDS { get; set; }

        [Option(Required = false, Default = null, HelpText = "AWS profile or omit to use default credentials")]
        public string AWSProfile { get; set; }

        [Option(Required = false, Default = null, HelpText = "AWS region or omit to use default, e.g. us-west-1, us-gov-west-1")]
        public string AWSRegion { get; set; }
       
        [Option(Required = false, Default = -1, HelpText = "Limit the number of concurrent downloads, negative to use all available cores")]
        public int ConcurrentDownloads { get; set; }

        [Option(Required = false, Default = false, HelpText = "Overwrite existing files")]
        public bool Overwrite { get; set; }

        [Option(Required = false, Default = Mission.None, HelpText = "Mission flag enables mission specific behavior, e.g. None, MSL, M2020")]
        public Mission Mission { get; set; }
    }

    public class FetchData
    {
        private FetchDataOptions options;
        private MissionSpecific mission;

        private static readonly ILog logger = LogManager.GetLogger(typeof(FetchData));

        private string[] defaultSearchLocations = new string[]
        {
            "s3://red-product/ods/surface/sol/#####/soas/rdr", //mslice bucket on us-west-1 (malin images??)
            "s3://red-product/proj/msl/redops/ods/surface/sol/#####/opgs/rdr", //mslice bucket on us-west-1
            "s3://m20-roastt-staging/ocs/test/sol/#####/ids/rdr" //M2020 bucket on us-gov-west-1
            //see https://github.jpl.nasa.gov/OnSight/Landform/wiki/M2020-Data-Notes
        };
        
        public FetchData(FetchDataOptions opts)
        {
            options = opts;
            if (options.SearchLocations == null || options.SearchLocations.Count() == 0)
            {
                options.SearchLocations = defaultSearchLocations;
            }
            options.WithPNG |= options.WithOBJ;
            
            mission = MissionSpecific.GetInstance(options.Mission);
        }

        //TODO: this is public because it's also used by EmtToScene
        //if/when that goes away consider making this private
        public IEnumerable<string> IndexFiles(string searchDir)
        {
            try
            {
                List<string> results = new List<string>();
                logger.InfoFormat("searching \"{0}\"", searchDir);
                var inputStorageHelper = new StorageHelper(options.AWSProfile, options.AWSRegion);
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
                logger.InfoFormat("error searching \"{0}\": {1}", searchDir, e.Message);
                return new string[] { };
            }
        }

        private string[] ExpandSolSpecifier(string solString)
        {
            string[] parts = solString.Split(',');
            List<int> sols = new List<int>();
            foreach (var part in parts)
            {
                if (part.Contains('-'))
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
            return sols.Distinct().OrderBy(x => x).Select(x => x.ToString("00000")).ToArray();
        }

        private List<string> Filter(List<string> products)
        {
            List<string> result = new List<string>();
            var acceptedSiteDrives = SiteDrive.ParseList(options.SiteDrives);
            var acceptedProductIds = ProductIDFilter();
            var acceptedExtensions = GetExtensions();
            foreach (var p in products)
            {
                string ext = Path.GetExtension(p).ToUpper();
                if (acceptedExtensions.Contains(ext))
                {
                    string filename = Path.GetFileNameWithoutExtension(p);
                    if ((mission == null || mission.CheckFilename(filename)) &&
                        (acceptedProductIds == null || acceptedProductIds.Contains(filename)))
                    {
                        SiteDrive? sd = GetSiteDrive(filename);
                        if (acceptedSiteDrives.Length == 0 || !sd.HasValue ||
                            acceptedSiteDrives.Any(asd => asd == sd.Value))
                        {
                            result.Add(p);
                        }
                    }
                }
            }
            logger.InfoFormat("filtered {0}->{1} products, site drives {2}, extensions {3}, {4} specific product ids",
                              products.Count, result.Count,
                              acceptedSiteDrives.Count() > 0 ? String.Join(",", acceptedSiteDrives) : "(all)",
                              String.Join(",", acceptedExtensions.ToList()),
                              acceptedProductIds != null ? acceptedProductIds.Count.ToString() : "no");
            return result;
        }

        private HashSet<string> ProductIDFilter()
        {
            if (options.Include == null)
            {
                return null;
            }
            return new HashSet<string>(File.ReadAllLines(options.Include)
                                       .Where(s => !string.IsNullOrEmpty(s.Trim()))
                                       .Select(s => Path.GetFileNameWithoutExtension(s)));
        }

        private HashSet<string> GetExtensions()
        {
            var ret = new HashSet<string>();
            if (!options.NoPDS)
            {
                ret.Add(".IMG");
                ret.Add(".LBL");
            }
            if (options.WithPNG)
            {
                ret.Add(".PNG");
            }
            if (options.WithOBJ)
            {
                ret.Add(".OBJ");
                ret.Add(".MTL");
            }
            return ret;
        }

        private SiteDrive? GetSiteDrive(string filename)
        {
            var id = RoverProductId.ParseFromString(filename);
            if (id == null || id.Producer != RoverProductProducer.OPGS)
            {
                return null;
            }
            return ((OPGSProductId)id).SiteDrive;
        }

        private string LocalPath(string url)
        {
            string dir = ""; //Path.Combine() ignores zero length strings
            if (!options.NoSubdirs)
            {
                dir = StringHelper.StripProtocol(StringHelper.StripLastUrlPathSegment(StringHelper.NormalizeUrl(url)));
            }
            return Path.Combine(options.OutputDir, dir, StringHelper.GetLastUrlPathSegment(url));
        }

        private void DownloadFile(string url)
        {
            var localPath = LocalPath(url);    
            PathHelper.EnsureExists(Path.GetDirectoryName(localPath));
            bool s3 = url.ToLower().StartsWith("s3");
            string filename = StringHelper.GetLastUrlPathSegment(url);
            TemporaryFile.GetAndMove(localPath, f =>
            {
                bool success = false;
                int retryCounter = 0;
                while (!success && retryCounter < 3)
                {
                    if (retryCounter > 0)
                    {
                        logger.InfoFormat("retrying download \"{0}\"", filename);
                    }
                    retryCounter++;
                    try
                    {
                        if (s3)
                        {
                            var inputStorageHelper = new StorageHelper(options.AWSProfile, options.AWSRegion);
                            success = inputStorageHelper.DownloadFile(url, f);
                        }
                        else
                        {
                            using (var fs = new FileStream(localPath, FileMode.Create))
                            {
                                WebRequest.Create(url).GetResponse().GetResponseStream().CopyTo(fs);
                                success = true;
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        logger.InfoFormat("error downloading \"{0}\": {1}", filename, e.Message);
                    }
                    if (!success)
                    {
                        logger.InfoFormat("failed to download \"{0}\"", filename);
                    }
                }
            });
        }

        private bool ShouldDownload(string url)
        {
            if (options.Overwrite == true || !url.ToLower().StartsWith("s3://"))
            {
                return true;
            }
            var inputStorageHelper = new StorageHelper(options.AWSProfile, options.AWSRegion);
            var localPath = LocalPath(url);
            return !inputStorageHelper.FileSizeMatches(url, localPath);
        }

        private void DownloadFiles(List<string> files)
        {
            var po = new ParallelOptions() { MaxDegreeOfParallelism = options.ConcurrentDownloads };

            var totalFilesToDownload = files;
            var remainingFilesToDownload = totalFilesToDownload;
            if (!options.Overwrite)
            {
                ConcurrentBag<string> toDownload = new ConcurrentBag<string>();
                CoreLimitedParallel.ForEach(totalFilesToDownload, po, f =>
                {
                    if (ShouldDownload(f))
                    {
                        toDownload.Add(f);
                    }
                });
                remainingFilesToDownload = toDownload.Distinct().ToList();
            }
            int total = totalFilesToDownload.Count();
            int remaining = remainingFilesToDownload.Count();
            int downloaded = 0;
            logger.InfoFormat("{0} files, {1} already downloaded, {2} to go", total, total - remaining, remaining);
            CoreLimitedParallel.ForEach(remainingFilesToDownload, po, f =>
            {
                DownloadFile(f);
                Interlocked.Increment(ref downloaded);
                logger.InfoFormat("downloaded \"{0}\" {1}/{2} {3}%", Path.GetFileName(f),
                                  downloaded, remaining, (downloaded * 100) / remaining);
            });
        }

        public int Run()
        {
            if (options.Raw)
            {
                DownloadFiles(options.Input.Split(',').Select(f => f.Trim()).Where(f => f != "").ToList());
            }
            else
            {
                var sols = ExpandSolSpecifier(options.Input);
                logger.InfoFormat("seaching sols {0}", string.Join(", ", sols));
                
                var solToProducts = new ConcurrentDictionary<string, List<string>>();
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
                foreach (var sol in sols)
                {
                    logger.InfoFormat("filtering files for sol {0}", sol);
                    solToProducts[sol] = Filter(solToProducts[sol]);
                }
                
                DownloadFiles(solToProducts.SelectMany(s => s.Value).ToList());
            }

            return 0;
        }
    }
}
