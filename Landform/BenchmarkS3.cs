using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using CommandLine;
using log4net;
using JPLOPS.Cloud;
using JPLOPS.Util;
using JPLOPS.Pipeline;

/// <summary>
/// Utility to benchmark S3 performance.
///
/// Example:
///
/// Landform.exe benchmarks3 s3://bucket/ods/roastt/sol --awsprofile=credss-default --awsregion=us-gov-west-1
///   --maxfiles=10
///
/// </summary>
namespace JPLOPS.Landform
{
    [Verb("benchmark-s3", HelpText = "Run benchmark to test S3 speeds")]
    public class BenchmarkS3Options
    {
        [Value(0, Required = true, HelpText = "S3 base url, e.g. s3://BUCKET/ods/VENUE/sol")]
        public string S3Url { get; set; }

        [Option(Default = "*.IMG", HelpText = "S3 filter, matches whole path, may contain wildcards * and ?")]
        public string Filter { get; set; }

        [Option(Default = 50, HelpText = "Max files to test, unlimited if negative or 0")]
        public int MaxFiles { get; set; }

        [Option(Default = null, HelpText = "AWS profile or omit to use default credentials (can be \"none\")")]
        public string AWSProfile { get; set; }

        [Option(Default = null, HelpText = "AWS region or omit to use default, e.g. us-west-1, us-gov-west-1 (can be \"none\")")]
        public string AWSRegion { get; set; }

        [Option(Default = "None", HelpText = "Mission flag enables mission specific behavior, optional :venue override, e.g. None, MSL, M2020, M20SOPS, M20SOPS:dev, M20SOPS:sbeta")]
        public string Mission { get; set; }
    }

    public class BenchmarkS3
    {
        public const int RANDOM_READS_PER_FILE = 5;
        public const int MAX_RANDOM_READ = 1024 * 64;

        private static readonly ILog logger = LogManager.GetLogger(typeof(FetchData));

        private BenchmarkS3Options options;

        private StorageHelper storageHelper;

        private class FileInfo
        {
            public string url;
            public long size;

            public FileInfo(string url)
            {
                this.url = url;
            }
        }

        private List<FileInfo> files = new List<FileInfo>();

        private class DownloadInfo
        {
            public string what;
            public long count;
            public long ms;

            public DownloadInfo(string what, long count, Stopwatch sw)
            {
                this.what = what;
                this.count = count;
                this.ms = sw.ElapsedMilliseconds;
            }

            public DownloadInfo(FileInfo fi, long bytes, Stopwatch sw)
            {
                this.what = fi.url;
                this.count = bytes;
                this.ms = sw.ElapsedMilliseconds;
            }

            public string Format()
            {
                return string.Format(what, count, ms * 1e-3, count / (ms * 1e-3));
            }
        }
                
        public BenchmarkS3(BenchmarkS3Options options)
        {
            this.options = options;

            var mission = MissionSpecific.GetInstance(options.Mission);

            if (mission != null)
            {
                if (string.IsNullOrEmpty(options.AWSRegion))
                {
                    options.AWSRegion = mission.GetDefaultAWSRegion();
                }
                if (string.IsNullOrEmpty(options.AWSProfile))
                {
                    options.AWSProfile = mission.GetDefaultAWSProfile();
                }
            }

            storageHelper = new StorageHelper(options.AWSProfile, options.AWSRegion, logger);
        }

        private List<DownloadInfo> GenerateFileList()
        {
            var ret = new List<DownloadInfo>();

            string s3Url = options.S3Url.TrimEnd('/') + "/";
            Console.WriteLine("listing subdirs of {0}", s3Url);

            var sw = Stopwatch.StartNew();
            var folders = storageHelper.SearchObjects(s3Url, recursive: false, folders: true, files: false).ToList();
            sw.Stop();
            Console.WriteLine("found {0} folders in {1}s, {2:F3} folders/s",
                              folders.Count, sw.ElapsedMilliseconds * 1e-3,
                              Fmt.Bytes(folders.Count / (sw.ElapsedMilliseconds * 1e-3)));
            var di = new DownloadInfo("listed {0} subfolders in {1:F3}s, {2:F3}/s", folders.Count, sw);
            ret.Add(di);
            Console.WriteLine(di.Format());

            Console.WriteLine("recursively listing {0} folders in parallel", folders.Count);
            long totalFiles = 0;
            sw = Stopwatch.StartNew();
            CoreLimitedParallel.ForEach(folders, folder =>
            {
                var fi = new List<FileInfo>();
                foreach (var url in storageHelper.SearchObjects(folder, options.Filter, recursive: true))
                {
                    fi.Add(new FileInfo(url));
                }
                lock (files)
                {
                    files.AddRange(fi);
                    totalFiles += fi.Count;
                }
            });
            sw.Stop();
            di = new DownloadInfo("recursively listed {0} files in parallel in {1:F3}s, {2:F3}/s", totalFiles, sw);
            ret.Add(di);
            Console.WriteLine(di.Format());

            Console.WriteLine("randomizing files");
            var rng = NumberHelper.MakeRandomGenerator();
            for(int i = 0; i < files.Count; i++)
            {
                int j = rng.Next(0, files.Count);
                var tmp = files[i];
                files[i] = files[j];
                files[j] = tmp;
            }

            if (options.MaxFiles > 0 && files.Count > options.MaxFiles)
            {
                Console.WriteLine("taking subsample of {0} files", options.MaxFiles);
                files = files.GetRange(0, options.MaxFiles);
            }

            Console.WriteLine("getting sizes of {0} files in parallel", files.Count);
            sw = Stopwatch.StartNew();
            CoreLimitedParallel.ForEach(files, fi =>
            {
                fi.size = storageHelper.FileSize(fi.url);
            });
            sw.Stop();
            di = new DownloadInfo("got sizes of {0} files in parallel in {1:F3}s, {2:F3}/s", files.Count, sw);
            ret.Add(di);
            Console.WriteLine(di.Format());

            return ret;
        }

        private List<DownloadInfo> ProfileHeaderReads()
        {
            Console.WriteLine("testing header read of {0} files", files.Count);
            var ret = new List<DownloadInfo>();
            long totalBytes = 0;
            var tsw = Stopwatch.StartNew();
            for (int i = 0; i < files.Count; i++)
                
            {
                long bytes = 0;
                var sw = Stopwatch.StartNew();
                storageHelper.GetStorageStream(files[i].url, stream =>
                {
                    using (var reader = new StreamReader(stream))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            bytes += line.Length + 1;
                            if (line == "END")
                            {
                                break;
                            }
                        }
                    }
                });
                sw.Stop();
                ret.Add(new DownloadInfo(files[i], bytes, sw));
                totalBytes += bytes;
                Console.WriteLine("tested file {0}/{1}, {2:F3} bytes/s, {3:F3} bytes/s cumulative", i + 1, files.Count,
                                  Fmt.Bytes(bytes / (sw.ElapsedMilliseconds * 1e-3)),
                                  Fmt.Bytes(totalBytes / (tsw.ElapsedMilliseconds * 1e-3)));
            }
            return ret;
        }

        private List<DownloadInfo> ProfileFullReads()
        {
            Console.WriteLine("testing full read of {0} files", files.Count);
            var ret = new List<DownloadInfo>();
            long totalBytes = 0;
            var tsw = Stopwatch.StartNew();
            for (int i = 0; i < files.Count; i++)
            {
                long bytes = 0;
                var sw = Stopwatch.StartNew();
                storageHelper.GetStream(files[i].url, stream =>
                {
                    var mem = new MemoryStream();
                    stream.CopyTo(mem);
                    bytes = mem.Length;
                });
                sw.Stop();
                ret.Add(new DownloadInfo(files[i], bytes, sw));
                totalBytes += bytes;
                Console.WriteLine("tested file {0}/{1}, {2:F3} bytes/s, {3:F3} bytes/s cumulative", i + 1, files.Count,
                                  Fmt.Bytes(bytes / (sw.ElapsedMilliseconds * 1e-3)),
                                  Fmt.Bytes(totalBytes / (tsw.ElapsedMilliseconds * 1e-3)));
            }
            return ret;
        }

        private List<DownloadInfo> ProfileRandomReads()
        {
            Console.WriteLine("testing {0} random reads in {1} files", RANDOM_READS_PER_FILE, files.Count);
            var ret = new List<DownloadInfo>();
            long totalBytes = 0;
            var tsw = Stopwatch.StartNew();
            var rng = NumberHelper.MakeRandomGenerator();
            var buffer = new byte[1024 * 1024];
            for (int i = 0; i < files.Count; i++)
            {   
                long bytes = 0;
                var sw = Stopwatch.StartNew();
                for (int j = 0; j < RANDOM_READS_PER_FILE; j++)
                {
                    long bytesRead = 0, newBytes = -1;
                    long start = rng.Next(0, (int)Math.Min(int.MaxValue, files[i].size));
                    long max = rng.Next(1, (int)Math.Min(files[i].size, MAX_RANDOM_READ));
                    var rsw = Stopwatch.StartNew();
                    storageHelper.GetStorageStream(files[i].url, stream =>
                    {
                        while (bytesRead < max && newBytes != 0)
                        {
                            newBytes = stream.Read(buffer, 0,  Math.Min(buffer.Length, (int)(max - bytesRead)));
                            bytesRead += newBytes;
                        }
                    }, start);
                    rsw.Stop();
                    bytes += bytesRead;
                    ret.Add(new DownloadInfo(files[i], bytes, rsw));
                }
                sw.Stop();
                totalBytes += bytes;
                Console.WriteLine("tested file {0}/{1}, {2:F3} bytes/s, {3:F3} bytes/s cumulative", i + 1, files.Count,
                                  Fmt.Bytes(bytes / (sw.ElapsedMilliseconds * 1e-3)),
                                  Fmt.Bytes(totalBytes / (tsw.ElapsedMilliseconds * 1e-3)));
            }
            return ret;
        }

        public int Run()
        {
            var sw = Stopwatch.StartNew();

            var fl = GenerateFileList();

            var hr = ProfileHeaderReads();
            var rr = ProfileRandomReads();
            var fr = ProfileFullReads();
            
            Console.WriteLine("-- {0} total elapsed time --", Fmt.HMS(sw.ElapsedMilliseconds));

            foreach (var di in fl)
            {
                Console.WriteLine(di.Format());
            }

            string[] what = new string[] { "header", "random", "full" };
            List<DownloadInfo>[] info = new List<DownloadInfo>[] { hr, rr, fr };
            for (int i = 0; i < info.Length; i++)
            {
                double sec = info[i].Sum(di => di.ms * 1e-3);
                long bytes = info[i].Sum(di => di.count);
                Console.WriteLine("{0} {1} reads in {2:F3} s, {3:F3} reads/s, {4:F3} bytes/s",
                                  info[i].Count, what[i], sec, info[i].Count / sec, Fmt.Bytes(bytes / sec));
            }

            return 0;
        }
    }
}
