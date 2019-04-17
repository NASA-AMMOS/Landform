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

namespace QMDTMesher
{
    class QMDTMesher
    {
        static ILog logger = LogManager.GetLogger(typeof(QMDTMesher));


        public class Options
        {
            [Option("normal-radius", Default = 4.2, Required = false, HelpText = "Radius to use when generating normals for meshing")]
            public double NormalRadius { get; set; }

            [Option("roughness-radius", Default = 4.2, Required = false, HelpText = "Radius to use when selecting nearby points to compute roughness")]
            public double RoughnessRadius { get; set; }

            [Option("vertex-scale", Default = 1, Required = false, HelpText = "Size of each vertex to use when generating mesh")]
            public double VertexScale { get; set; }

            [Option("nomesh", Default = false, Required = false, HelpText = "If set the input point cloud won't be meshed")]
            public bool NoMesh { get; set; }

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

            //Configure gdal
            GdalConfiguration.ConfigureGdal();


            Parser.Default.ParseArguments<Options>(args)
                   .WithParsed<Options>(opt =>
                   {
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

                       logger.Info("Generating mesh from scans:");
                       foreach (var s in scans)
                       {
                           logger.Info(s);
                       }
                       logger.Info("normal-radius:    " + opt.NormalRadius);
                       logger.Info("roughness-radius: " + opt.RoughnessRadius);
                       logger.Info("vertex-scale:     " + opt.VertexScale);
                       Run(scans, opt);
                   });
        }

        static void Run(List<string> scans, Options opt)
        {
            logger.Info("Loading scans");
            Mesh[] meshes = new Mesh[scans.Count];
            CoreLimitedParallel.For(0, scans.Count, i =>
            {
                meshes[i] = LoadScan(scans[i], opt.NormalRadius);
            });
            logger.Info("Merging scans");
            var combined = new Mesh(hasNormals: true);
            combined.MergeWith(meshes);
            logger.Info("Generating mesh");
            var mesh = opt.NoMesh ? combined : FSSR.Reconstruct(combined, (float)opt.VertexScale);
            var roughness = new PointCloudRoughness(mesh, combined);
            logger.Info("Calculating roughness");
            var rmesh = roughness.CalculateRoughness(opt.NormalRadius, new ProgressReporter<int>(1, i=> logger.Info("Progress: " + i + "%")));
            PLYSerializer.Write(rmesh, opt.OutputMesh, plyWriter: new PointCloudRoughness.RoughnessPlyWriter(true));
        }



        static Mesh LoadScan(string filename, double normalRadius)
        {
            var m = Mesh.Load(filename);
            m.RemoveDuplicateVertices();
            //m = CloudCompare.SORCleaning(m, 6, 1);
            //ComputeCameraPosEstimate(EstimateCameraPos(m)).Save(@"D:\ReconstructionProjects\M2020\QMDT\SampleRock\camera.ply");
            m = ComputeNormals(m, normalRadius);
            //m.Save(@"D:\ReconstructionProjects\M2020\QMDT\SampleRock\test.ply");
            return m;
        }

        static Mesh ComputeNormals(Mesh m, double normalRadius)
        {
            //var mwn = MeshLab.ComputeNormals(m);
            var mwn = CloudCompare.GenerateNormals(m, normalRadius);
            mwn.FlipNormalsWithRespectToPoint(EstimateCameraPos(m));
            mwn.RemoveZeroLengthNormals();
            //mwn = CloudCompare.OrientNormals(mwn, 10);
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
