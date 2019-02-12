using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OPS.Util;
using OPS.Pipeline.AlignmentServer;

namespace OPS.Pipeline
{
    public class IngestAlignmentInputs : PipelineRoutine
    {
        public class BaseUrl
        {
            public readonly string Url;
            public readonly bool Recursive;
            public BaseUrl(string url)
            {
                if (url.EndsWith("/**"))
                {
                    Url = url.Substring(0, url.Length - 2); //leave trailing slash
                    Recursive = true;
                }
                else
                {
                    Url = StringHelper.EnsureTrailingSlash(url);
                    Recursive = false;
                }
            }
        }
        public readonly List<BaseUrl> BaseUrls = new List<BaseUrl>();

        private Project project;
        private IngestPDSImage ingester;

        public IngestAlignmentInputs(PipelineCore pipeline, Project project, bool recreateExistingObservations = false,
                                     bool resetExistingTransforms = false)
            : base(pipeline)
        {
            if (string.IsNullOrEmpty(project.InputPath))
            {
                throw new ArgumentException("input path not set for project " + project.Name);
            }

            if (project.InputPath.ToLower().EndsWith(".txt"))
            {
                pipeline.GetFile(project.InputPath, file => {
                        foreach (var line in File.ReadAllLines(file))
                        {
                            var url = line.Trim();
                            if (url != "")
                            {
                                BaseUrls.Add(new BaseUrl(url));
                            }
                        }
                    });
            }
            else if (project.InputPath.EndsWith(".json"))
            {
                pipeline.GetFile(project.InputPath, file => {
                        foreach (var url in JsonHelper.FromJson<List<string>>(File.ReadAllText(file), autoTypes: false))
                        {
                            BaseUrls.Add(new BaseUrl(url));
                        }
                    });
            }
            else
            {
                BaseUrls.Add(new BaseUrl(project.InputPath));
            }

            this.project = project;

            ingester = new IngestPDSImage(pipeline, project, recreateExistingObservations, resetExistingTransforms);
        }

        public int Ingest(MSLLocations locations, Action<IngestImage.Result> func = null)
        {
            ingester.Locations = locations;
            int n = 0;
            ConcurrentDictionary<SiteDrive, ConcurrentDictionary<string, int>> stats =
                new ConcurrentDictionary<SiteDrive, ConcurrentDictionary<string, int>>();
            foreach (var entry in BaseUrls)
            {
                pipeline.LogInfo("{0}ingesting input files from {1} for alignment project {2}",
                                 entry.Recursive ? "recursively " : "", entry.Url, project.Name);
        
                Parallel.ForEach(pipeline.SearchFiles(entry.Url, "*.IMG", recursive: entry.Recursive), url => {

                        var res = ingester.Ingest(url);

                        //TODO why do we care about duplicates?
                        //https://github.jpl.nasa.gov/OnSight/Landform/issues/408
                        if (res.Status == IngestImage.Status.Added || res.Status == IngestImage.Status.Duplicate)
                        {
                            Interlocked.Increment(ref n);

                            if (res.Status == IngestImage.Status.Skipped)
                            {
                                pipeline.LogInfo("{0} ({1})", res.ImageUrl, res.Status);
                            }
                            else if (res.Observation is RoverObservation)
                            {
                                var obs = res.Observation as RoverObservation;
                                var sd = new SiteDrive(obs.Site, obs.Drive);
                                var sds = stats.GetOrAdd(sd, _ => new ConcurrentDictionary<string, int>());
                                sds.AddOrUpdate(obs.ObservationType, _ => 1, (_, m) => m + 1);
                                pipeline.LogInfo("{0} ({1}) {2}x{3} {4} sitedrive={5} -> observation {6}", res.ImageUrl,
                                                 res.Status, obs.Width, obs.Height, obs.ObservationType, sd, obs.Name);
                            }
                            else if (res.Observation != null)
                            {
                                var obs = res.Observation;
                                pipeline.LogInfo("{0} ({1}) {2}x{3} {4} -> observation {5}", res.ImageUrl,
                                                 res.Status, obs.Width, obs.Height, obs.ObservationType, obs.Name);
                            }
                            else
                            {
                                pipeline.LogInfo("{0} ({1}) -> observation NULL", res.ImageUrl, res.Status);
                            }

                            if (func != null) func(res);
                        }
                    });
            }
                
            pipeline.LogInfo("ingested {0} input files for alignment project {1}", n, project.Name);
            foreach (var sds in stats)
            {
                pipeline.LogInfo("sitedrive {0}: {1}", sds.Key,
                                 string.Join(", ",
                                             sds.Value.Select(s => s.Value + " " + s.Key + " observations").ToArray()));
            }

            return n;
        }
    }
}
