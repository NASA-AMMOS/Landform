using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Cloud;
using CommandLine;
using OPS.Imaging;
using OPS.Util;

namespace OPS.Pipeline
{
    [Verb("crawlmsl", HelpText = "Crawl MSL S3 bucket for dataproducts and add them to the landform database")]
    public class CralwMSLOptions
    {
        [Option(Required =true, HelpText = "Comma seperated url and port of database server (example: http:\\thedatabase.com,1433")]
        public string DatabaseLocation { get; set; }

        [Option(Required = true, HelpText = "Name of the database to connect to")]
        public string DatabaseName { get; set; }

        [Option(Required = true, HelpText = "Username to use when connecting to database")]
        public string DatabaseUser { get; set; }

        [Option(Required = true, HelpText = "Password to use when connecting to database")]
        public string DatabasePassword { get; set; }

        [Option(Required = true, HelpText = "Name of the aws profile to use to authenticate")]
        public string AwsProfile { get; set; }

    }

    public class MSLProject
    {
        public const string PROJECT_NAME = "MSL";
        public const string ROOT_FRAME_NAME = "root";

    }

    /// <summary>
    /// The crawl MSL command 
    /// </summary>
    public class CrawlMSL
    {       

        CralwMSLOptions options;

        public CrawlMSL(CralwMSLOptions options)
        {
            this.options = options;
        }

        public string SiteDriveFrameName(PDSParser parser)
        {
            return parser.SiteDrive;
        }

        public string ObservationFrameName(PDSParser parser)
        {
            return parser.Instrument.ToString() + "_" + parser.RMC;
        }
        
        public string ObservationName(PDSParser parser)
        {
            return parser.ProductIdString;
        }

        public int Run()
        {
            List<PDSDerivedImageType> obsevationTypeLookup = new List<PDSDerivedImageType>();
            obsevationTypeLookup.Add(PDSDerivedImageType.Image);
            obsevationTypeLookup.Add(PDSDerivedImageType.Range);
            obsevationTypeLookup.Add(PDSDerivedImageType.XYZ);

            object lockobject = new object();

            LandformDatabase database = new LandformDatabase(options.DatabaseLocation, options.DatabaseName, options.DatabaseUser, options.DatabasePassword);
            using (LandformDbContext context = database.CreateContext())
            {
                Project project = context.FindOrCreateProject(MSLProject.PROJECT_NAME);
                Frame rootFrame = context.FindOrCreateFrame(project, MSLProject.ROOT_FRAME_NAME);
                StorageHelper storage = new StorageHelper(options.AwsProfile, true);
                foreach (var dir in storage.SearchFolders("s3://m20-ocs-dev/ods/surface/sol"))
                {
                    Console.WriteLine(dir);

                    Parallel.ForEach(storage.SearchObjects(dir, "*opgs/rdr*.IMG", true), url =>
                    //foreach (var url in storage.SearchObjects("s3://m20-ocs-dev/ods/surface/sol/00583/opgs/rdr/ncam", "*.IMG", true))
                    {
                        string status = "";
                        storage.GetStream(url, stream =>
                        {
                            try
                            {
                                PDSMetadata metadata = new PDSMetadata(stream);
                                PDSParser parser = new PDSParser(metadata);
                                lock (lockobject)
                                {
                                    if (obsevationTypeLookup.Contains(parser.DerivedImageType) && parser.GeometricProjection == PDSGeometryProjection.Raw && parser.ImageSizeType == PDSImageSizeType.Regular)
                                    {
                                        Frame siteDriveFrame = context.FindOrCreateFrame(project, SiteDriveFrameName(parser));
                                        Frame observationFrame = context.FindOrCreateFrame(project, ObservationFrameName(parser));
                                        string observationName = ObservationName(parser);
                                        Observation observation = context.FindObservation(project, observationName);

                                        if (observation == null)
                                        {
                                            status = "Add";
                                            string cameraModel = JsonHelper.ToJson(metadata.CameraModel);
                                            observation = new RoverObservation(project, siteDriveFrame, observationName, url, parser.DerivedImageType.ToString(), cameraModel, parser.Site, parser.Drive, int.Parse(parser.ProductId.ver), parser.Instrument.ToString(), parser.ImageSizeType.ToString());
                                            context.AddObservation(observation);
                                        }
                                        else
                                        {
                                            status = "Exists";
                                        }
                                    }
                                    else
                                    {
                                        status = "Skip";
                                    }
                                }
                            }
                            catch (Exception e)
                            {
                                status = "Fail";
                                Console.WriteLine(e.Message);
                            }
                            Console.WriteLine(url + "\t" + status);
                        });
                    });
                }
            }
            return 0;
        }
    }
}
