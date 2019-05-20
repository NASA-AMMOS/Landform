using CommandLine;
using log4net;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Cloud;
using OPS.Util;
using System.IO;
using OPS.Pipeline.TileServer;
using OPS.Geometry;
using OPS.Imaging;
using Microsoft.Xna.Framework;
using System.Threading;

namespace OPS.Pipeline
{
    /// "Stop Trying to Make Fetch Happen"
    ///   - Regina George (Mean Girls)

    [Verb("fetch", HelpText = "Convert emt data into an ASTTRO scene")]
    public class FetchDataOptions
    {

        [Value(0, Required = true, Default = null, HelpText = "'27-32' or '607,609' or a mixture '27-32,607,609-611'")]
        public string Sols { get; set; }

        [Value(1, Required = true, Default = null, HelpText = "output directory, e.g. c:/Users/$USERNAME/Downloads")]
        public string OutputDir { get; set; }
        
        [Value(2, Required = false, HelpText = "RDR search locations with sol replaced with ##### (ie s3://m20-roastt-staging/ocs/test/sol/#####/ids/rdr/")]
        public IEnumerable<string> SearchLocations { get; set; } = null;
        
        [Option(Required = true, HelpText = "")]
        public string InputAWSProfile { get; set; }

        [Option(Required = false, Default = "us-gov-west-1", HelpText = "")]
        public string InputAWSRegion { get; set; }
       
        [Option(Required = false, Default = -1, HelpText = "Control the number of concurrent downloads")]
        public int ConcurrentDownloads { get; set; }

        [Option(Required = false, Default = false, HelpText = "Overwrite existing files")]
        public bool Overwrite { get; set; }
    }

    public class FetchData
    {
        FetchDataOptions options;

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
            if (options.Overwrite || !File.Exists(localPath))
            {
                PathHelper.EnsureExists(Path.GetDirectoryName(localPath));
                TemporaryFile.GetAndMove(localPath, f =>
                {
                    try
                    {
                        var inputStorageHelper = new StorageHelper(options.InputAWSProfile, options.InputAWSRegion);
                        inputStorageHelper.DownloadFile(s3Location, f);
                    }
                    catch (Exception e)
                    {
                        logger.Info("\tError downloading: " + Path.GetFileName(s3Location));
                        logger.Info("\t" + e.Message);
                    }
                });
            }
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
            foreach(var p in products)
            {
                string filename = Path.GetFileNameWithoutExtension(p);
                string ext = Path.GetExtension(p).ToUpper();
                if(extensions.Contains(ext) && IngestPDSImage.CheckFilename(filename, false))
                {
                    result.Add(p);
                }
            }
            return result;
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
            var totalFilesToDownload = solToProducts.SelectMany(s => s.Value);
            var remainingFilesToDownload =
                options.Overwrite ? totalFilesToDownload : totalFilesToDownload.Where(s => !File.Exists(LocalPath(s)));
            logger.Info("Found " + (totalFilesToDownload.Count() - remainingFilesToDownload.Count()) + " on disk");
            logger.Info("Downloading " + remainingFilesToDownload.Count() + " files");
            var po = new ParallelOptions() { MaxDegreeOfParallelism = options.ConcurrentDownloads };
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
