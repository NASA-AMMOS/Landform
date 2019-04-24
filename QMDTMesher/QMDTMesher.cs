using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;
using log4net;
using Microsoft.Xna.Framework;
using OPS.Geometry;
using OPS.Util;
using OPS.MathExtensions;
using System.Diagnostics;

namespace QMDTMesher
{
    class QMDTMesher
    {
        static ILog logger = LogManager.GetLogger(typeof(QMDTMesher));


        public class Options
        {
            [Option("roughness-radius", Default = 13.25, Required = false, HelpText = "Radius to use when selecting nearby points to compute roughness")]
            public double RoughnessRadius { get; set; }

            [Option("normal-radius", Default = 13.25, Required = false, HelpText = "Radius to use when generating normals for meshing")]
            public double NormalRadius { get; set; }

            [Option("vertex-scale", Default = 1, Required = false, HelpText = "Size of each vertex to use when generating mesh")]
            public double VertexScale { get; set; }

            [Option("no-mesh", Default = false, Required = false, HelpText = "If set the input point cloud won't be meshed")]
            public bool NoMesh { get; set; }

            [Option("debug-output", Default = false, Required = false, HelpText = "Create a set of debug patches")]
            public bool DebugOutput { get; set; }

            [Option("subsample", Default = 0, Required = false, HelpText = "Subsample the data cloud at this spacing before computing roughness")]
            public double Subsample { get; set; }

            [Value(0, Required = true, HelpText = "Output mesh")]
            public string OutputMesh { get; set; }

            [Value(1, Required = true, HelpText = "A list of scans or directories containing scans to mesh")]
            public IEnumerable<string> ScanFiles { get; set; }
        }

        static void Main(string[] args)
        {
            //TODO centralize log4net initialization to uniformly handle --quiet and --logfile command line opts
            //https://github.jpl.nasa.gov/OnSight/Landform/issues/308
            Logging.ConfigureLogging();

            // Register filetype handlers
            new DAESerializer().Register();
            new OpenInventorSerializer().Register();
            new DracoSerializer().Register();

            //Configure gdal (uncomment below if image load/save is needed)
            //GdalConfiguration.ConfigureGdal();

            Stopwatch sw = new Stopwatch();
            sw.Start();
            Parser.Default.ParseArguments<Options>(args)
                   .WithParsed<Options>(opt =>
                   {
                       var area = opt.RoughnessRadius * opt.RoughnessRadius * Math.PI;

                       List<string> scans = new List<string>();
                       foreach (var f in opt.ScanFiles)
                       {
                           if (Directory.Exists(f))
                           {
                               // This is a directory, so add all scans beneath it
                               scans.AddRange(Directory.EnumerateFiles(f, "*.ply"));
                           }
                           else
                           {
                               // This is an individual scan
                               scans.Add(f);
                           }
                       }
                       if(opt.OutputMesh.ToUpper() == "AUTO")
                       {
                           var prefix = CommonPrefix(scans);
                           var outputDir = Path.Combine(Path.GetDirectoryName(prefix), "output");
                           opt.OutputMesh = Path.Combine(outputDir, string.Format("{0}{1}_R{2}mm_SUB{3}mm.ply", Path.GetFileName(prefix), opt.NoMesh ? "POINTS" : "MESH", opt.RoughnessRadius, opt.Subsample));
                       }
                       PathHelper.EnsureExists(Path.GetDirectoryName(opt.OutputMesh));
                       logger.Info("roughness-radius: " + opt.RoughnessRadius + " (area = " + Math.Round(area, 2) + ")");
                       logger.Info("normal-radius:    " + opt.NormalRadius);
                       logger.Info("vertex-scale:     " + opt.VertexScale);
                       logger.Info("subsample:        " + (opt.Subsample == 0 ? "NA" : opt.Subsample.ToString()));
                       logger.Info("output:           " + opt.OutputMesh);
                       logger.Info("inputs:");
                       foreach (var s in scans)
                       {
                           logger.Info("    " + Path.GetFileName(s));
                       }                       
                       Run(scans, opt);
                   });
            sw.Stop();
            logger.Info("Total execution time: " + sw.Elapsed);
        }


        static string CommonPrefix(IEnumerable<string> values)
        {
            return new string(values.First().Substring(0, values.Min(s => s.Length))
                              .TakeWhile((c, i) => values.All(s => s[i] == c)).ToArray());
        }


        static double ComputeScanDensity(Mesh m)
        {            
            VertexKDTree kd = new VertexKDTree(m.Vertices);
            RunningAverage ra = new RunningAverage();
            foreach (var v in m.Vertices)
            {
                foreach (var n in kd.NearestNeighbors(v.Position, 5))
                {
                    ra.Push(Vector3.Distance(n.Position, v.Position));
                }
            }
            return ra.Mean + ra.StandardDeviation / 2;
        }

        static void Run(List<string> scans, Options opt)
        {
            logger.Info("Loading scans");
            Mesh[] meshes = new Mesh[scans.Count];
            double[] pointSizes = new double[scans.Count];
            CoreLimitedParallel.For(0, scans.Count, i =>
            {
                meshes[i] = LoadScan(scans[i], opt.NormalRadius);
                pointSizes[i] = ComputeScanDensity(meshes[i]);
            });
            double averagePointSpacing = pointSizes.Average();
            logger.Info("Average point spacing: " + averagePointSpacing);
            logger.Info("Merging scans");
            var combined = new Mesh(hasNormals: true);
            combined.MergeWith(meshes);

            //Benchmark(combined);

            var mesh = combined;
            if (!opt.NoMesh)
            {
                logger.Info("Generating mesh");
                mesh = FSSR.Reconstruct(combined, (float)opt.VertexScale);
            }
            var dataCloud = combined;
            if(opt.Subsample != 0)
            {
                logger.Info("Subsampling data cloud");
                dataCloud = CloudCompare.Subsample(combined, (float)opt.Subsample);
                if(opt.DebugOutput)
                {
                    dataCloud.Save(opt.OutputMesh + ".subsampled.ply");
                }
            }
            logger.Info("Initilizing datastructures");
            var roughness = new PointCloudRoughness(mesh, dataCloud);
            var avgPointsPerPatch = roughness.EstimatedPointsPerPatch(opt.RoughnessRadius);
            logger.Info("Estimated points per patch: " + Math.Round(avgPointsPerPatch.Mean) + " (+-" + Math.Round(avgPointsPerPatch.StandardDeviation) + ")");
            if(opt.DebugOutput)
            {
                logger.Info("Writing debug patches");
                Random r = new Random(17);
                for(int i = 0; i < 10; i++)
                {
                    int vertIndex = r.Next(0, combined.Vertices.Count - 1);
                    var rn = roughness.CalculateRoughness(combined.Vertices[vertIndex], opt.RoughnessRadius, opt.OutputMesh + ".patch." + i + ".ply");
                    logger.Info("Patch:        " + i);
                    logger.Info("Position:     " + rn.Position);
                    logger.Info("Normal:       " + rn.Normal);
                    logger.Info("RMS:          " + rn.RMS);
                    logger.Info("Average Dist: " + rn.AverageDistance);
                    logger.Info("Variance:     " + rn.Variance);
                    logger.Info("Range:        " + rn.Range);
                    logger.Info("Center Dist:  " + rn.DistanceFromCenter);
                    logger.Info("-----------------------");
                }
            }
            logger.Info("Calculating roughness");
            var rmesh = roughness.CalculateRoughness(opt.RoughnessRadius, new ProgressReporter<int>(1, i=> logger.Info("Progress: " + i + "%")));
            PLYSerializer.Write(rmesh, opt.OutputMesh, plyWriter: new PointCloudRoughness.RoughnessPlyWriter(true));
        }


        static void Benchmark(Mesh m, int iterations = 1000)
        { 
            logger.Info("Benchmark");
            logger.Info("Creating MO");
            var mo = new MeshOperator(m, buildFaceTree: false, buildVertexTree: true, buildUVFaceTree: false);
            logger.Info("Creating KD");
            var kd = new VertexKDTree(m.Vertices);
            logger.Info("Starting Queries");
            Stopwatch sw = new Stopwatch();
            sw.Start();
            for(int i = 0; i < iterations; i++)
            {
                mo.NearestVerticesStrict(m.Vertices[0].Position, 20).ToArray();
            }
            sw.Stop();
            logger.Info("MeshOp " + sw.Elapsed);
            sw.Reset();
            sw.Start();
            for (int i = 0; i < iterations; i++)
            {
                kd.NearestDistance(m.Vertices[0].Position, 20).ToArray();
            }
            sw.Stop();
            logger.Info("KDTree " + sw.Elapsed);
        }

        static Mesh LoadScan(string filename, double normalRadius)
        {
            var m = Mesh.Load(filename);
            m.RemoveDuplicateVertices();
            m = ComputeNormals(m, normalRadius);
            return m;
        }

        static Mesh ComputeNormals(Mesh m, double normalRadius)
        {
            var mwn = CloudCompare.GenerateNormals(m, normalRadius);
            mwn.FlipNormalsTowardPoint(EstimateCameraPos(m));
            mwn.RemoveZeroLengthNormals();
            return mwn;
        }

        static Vector3 EstimateCameraPos(Mesh m)
        {
            var avg = Vector3.Zero;
            foreach (var v in m.Vertices)
            {
                avg += v.Position;
            }
            avg.Normalize();
            avg *= 2000;
            return avg;
        }

        static Mesh ComputeCameraPosEstimate(Vector3 avg)
        {
            var n = new Vector3(avg);
            n.Normalize();
            var rand = new Random();
            Mesh m = new Mesh();
            for (int i = 0; i < 1000; i++)
            {
                var p = new Vector3(avg) + new Vector3(rand.NextDouble(), rand.NextDouble(), rand.NextDouble()) * 50;
                m.Vertices.Add(new Vertex(p, n, new Vector4(0, 1, 0, 1), Vector2.Zero));
            }
            return m;
        }
    }
}
