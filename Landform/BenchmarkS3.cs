using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;
using OPS.Cloud;
using OPS.Util;

namespace OPS.Landform
{
    [Verb("benchmarks3", HelpText = "Run a benchmark against S3 to test read speed")]
    public class BenchmarkS3Options
    {
        [Value(0, Required = true, HelpText = "Output directory or a filename if generatefilelistfromprefix is specified")]
        public string Output { get; set; }

        [Value(1, Required = false, HelpText = "A list of files to profile reads against")]
        public string InputFile { get; set; }

        [Option(Required = true, HelpText = "AWS profile name")]
        public string Profile { get; set; }

        [Option(HelpText = "AWS region endpoint to connect to")]

        public string Endpoint { get; set; }

        [Option(HelpText = "Generate file list of random files under this url.  Url should represent a folder containg subfolders.")]
        public string GenerateFileListFromPrefix { get; set; }
    }

    /// <summary>
    /// Class for running simple benchmarks against S3 to test PDS image read speeds
    /// </summary>
    public class BenchmarkS3
    {
        const int FILES_TO_PROFILE = 500;
        const int MAX_FILES_TO_SAMPLE = 500000;
        const int RANDOM_READS_PER_FILE = 5;
        BenchmarkS3Options options;
                
        public BenchmarkS3(BenchmarkS3Options options)
        {
            this.options = options;
        }

        void GenerateFileList(StorageHelper storage)
        {
            string[] prefixes = storage.SearchFolders("s3://m20-ocs-dev/ods/surface/sol").ToArray();

            //string[] prefixes = File.ReadAllLines(options.InputFile);
            ConcurrentQueue<string> files = new ConcurrentQueue<string>();
            CoreLimitedParallel.ForEach(prefixes, prefix =>
            {
                if (files.Count > MAX_FILES_TO_SAMPLE)
                {
                    return;
                }
                Console.WriteLine(prefix);
                foreach (var url in storage.SearchObjects(prefix, "*.IMG", true))
                {
                    if (files.Count > MAX_FILES_TO_SAMPLE)
                    {
                        return;
                    }
                    files.Enqueue(url);
                }
                if (files.Count > MAX_FILES_TO_SAMPLE)
                {
                    return;
                }
            });
            var list = files.ToList();
            // Randomize order
            Random r = new Random();
            for(int i = 0; i < list.Count; i++)
            {
                int j = r.Next(0, list.Count);
                var tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
            }
            var result = list.GetRange(0, Math.Min(FILES_TO_PROFILE, list.Count));
            File.WriteAllLines(options.Output, result.ToArray());
        }

        void ProfileReadSpeed(StorageHelper storage)
        {
            PathHelper.EnsureExists(options.Output);
            string[] files = File.ReadAllLines(options.InputFile);
            {
                var headerTimings = ProfileHeaderReads(storage, files);
                string headerText = headerTimings.Select(sample => (sample.Item1 + "," + sample.Item2)).Aggregate((working, next) => working + "\n" + next);
                float headerAvg = headerTimings.Select(sample => sample.Item2).Sum() / (float)headerTimings.Count;
                Console.WriteLine("Time per Header:\t" + headerAvg + "ms");
                File.WriteAllText(Path.Combine(options.Output, "headers.csv"), "bytes,ms\n" + headerText);
            }
            {
                var partialTimings = ProfilePartialFileReads(storage, files);
                string partialText = partialTimings.Select(sample => (sample.Item1 + "," + sample.Item2)).Aggregate((working, next) => working + "\n" + next);
                float partialAvg = partialTimings.Where(sample=> sample.Item1 != 0).Select(sample => sample.Item2 / (float)sample.Item1).Sum() / (float)partialTimings.Count;
                Console.WriteLine("Partial per byte:\t" + partialAvg + "ms");
                File.WriteAllText(Path.Combine(options.Output,  "partial.csv"), "bytes,ms\n" + partialText);
            }
            {
                var fileTimings = ProfileFullFileReads(storage, files);
                string fileText = fileTimings.Select(sample => (sample.Item1 + "," + sample.Item2)).Aggregate((working, next) => working + "\n" + next);
                float fileAvg = fileTimings.Select(sample => sample.Item2).Sum() / (float)fileTimings.Count;
                Console.WriteLine("Time per File:\t" + fileAvg + "ms");
                File.WriteAllText(Path.Combine(options.Output, "files.csv"), "bytes,ms\n" + fileText);
            }
        }

        ConcurrentBag<Tuple<int, long>> ProfileHeaderReads(StorageHelper storage, string[] files)
        {
            Console.WriteLine("Header Read Test");
            ConcurrentBag<Tuple<int, long>> headerDownloadTimes = new ConcurrentBag<Tuple<int, long>>();
            Serial.ForEach(files, f =>
            {
                Stopwatch fileWatch = new Stopwatch();
                fileWatch.Start();
                int charCount = 0;
                
                storage.GetStorageStream(f, stream =>
                {
                    using (var reader = new StreamReader(stream))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            charCount += line.Length + 1;
                            if (line == "END")
                            {
                                break;
                            }
                        }
                    }
                });
                fileWatch.Stop();
                headerDownloadTimes.Add(new Tuple<int, long>(charCount, fileWatch.ElapsedMilliseconds));
                Console.Write("*");
            });
            Console.WriteLine("");
            return headerDownloadTimes;
        }

        ConcurrentBag<Tuple<long, long>> ProfileFullFileReads(StorageHelper storage, string[] files)
        {
            Console.WriteLine("Full File Read Test");
            ConcurrentBag<Tuple<long, long>> fileDownloadTimes = new ConcurrentBag<Tuple<long, long>>();
            Serial.ForEach(files, f =>
            {
                Stopwatch fileWatch = new Stopwatch();
                fileWatch.Start();
                long count = 0;
                storage.GetStream(f, stream =>
                {
                    var mem = new MemoryStream();
                    stream.CopyTo(mem);
                    count = mem.Length;

                });
                fileWatch.Stop();
                fileDownloadTimes.Add(new Tuple<long, long>(count, fileWatch.ElapsedMilliseconds));
                Console.Write("*");
            });
            Console.WriteLine("*");
            return fileDownloadTimes;
        }

        ConcurrentBag<Tuple<long, long>> ProfilePartialFileReads(StorageHelper storage, string[] files)
        {
            Console.WriteLine("Partial File Read Test");
            ConcurrentBag<Tuple<long, long>> partialDownloadTimes = new ConcurrentBag<Tuple<long, long>>();            
            Serial.ForEach(files, f =>
            {   
                Random r = new Random();
                for (int i = 0; i < RANDOM_READS_PER_FILE; i++)
                {
                    Stopwatch fileWatch = new Stopwatch();
                    fileWatch.Start();
                    long bytesRead = 0;
                    long start = r.Next(0, 52428);
                    long length = r.Next(1, 1024 * 64);
                    storage.GetStorageStream(f, stream =>
                    {
                        byte[] buffer = new byte[1024 * 1024];
                        while (bytesRead < length)
                        {
                            long newBytes = stream.Read(buffer, 0,  Math.Min(buffer.Length, (int)(length - bytesRead)));
                            bytesRead += newBytes;
                            if (bytesRead >= length || newBytes == 0)
                            {
                                break;
                            }
                        }
                    }, start);
                    fileWatch.Stop();
                    partialDownloadTimes.Add(new Tuple<long, long>(bytesRead, fileWatch.ElapsedMilliseconds));                    
                }
                Console.Write("*");
            });
            Console.WriteLine("");
            return partialDownloadTimes;
        }

        public int Run()
        {
            StorageHelper storage = new StorageHelper(options.Profile, options.Endpoint);
            if (options.GenerateFileListFromPrefix != null)
            {
                GenerateFileList(storage);
            }
            else
            {
                ProfileReadSpeed(storage);
            }            
            return 0;
        }
    }
}
