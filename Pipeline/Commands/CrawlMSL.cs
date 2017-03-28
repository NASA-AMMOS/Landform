using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Cloud;
using CommandLine;

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
    }

    public class CrawlMSL
    {
        LandformDatabase database;

        public CrawlMSL(CralwMSLOptions options)
        {
            database = new LandformDatabase(options.DatabaseLocation, options.DatabaseName, options.DatabaseUser, options.DatabasePassword);                 
        }

        public int Crawl()
        {
            using (LandformDbContext context = database.CreateContext())
            {
                Project project = context.Projects.Where(p => p.Name == "Test").FirstOrDefault();
                if (project == null)
                {
                    project = context.Projects.Add(new Project("Test"));
                }
                context.SaveChanges();
                Frame rootFrame = context.Frames.Where(f => f.Name == "RootFrame").FirstOrDefault();
                if (rootFrame == null)
                {
                    rootFrame = context.Frames.Add(new Frame(project.Id, "RootFrame"));
                }
                context.SaveChanges();
                Frame testFrame = context.Frames.Where(f => f.Name == "TestFrame").FirstOrDefault();
                if (testFrame == null)
                {
                    testFrame = context.Frames.Add(new Frame(project.Id, "TestFrame"));
                }
                context.SaveChanges();
                Observation observation = context.Observations.Where(obs => obs.Name == "imagename").FirstOrDefault();
                if (observation == null)
                {
                    observation = context.Observations.Add(new Observation(project.Id, testFrame.Id, "http://mrimageurl.jpg", "imagename", ObservationType.Image, "camera model"));
                }
                context.SaveChanges();
                RoverObservation roverObservation = (RoverObservation)context.Observations.Where(obs => obs.Name == "roverimage").FirstOrDefault();
                if (roverObservation == null)
                {
                    roverObservation = (RoverObservation)context.Observations.Add(new RoverObservation(project.Id, testFrame.Id, "http://mrimageurl.jpg", "roverimage", ObservationType.Image, "camera model", 1, 2, 0, "Hazcam", "Full"));
                }
                context.SaveChanges();
                FrameTransform transform = context.FrameTransforms.Where(trans => trans.FromFrameId == testFrame.Id).FirstOrDefault();
                if (transform == null)
                {
                    transform = context.FrameTransforms.Add(new FrameTransform(testFrame.Id, rootFrame.Id, new Microsoft.Xna.Framework.Vector3(), new Microsoft.Xna.Framework.Quaternion(), TransformSource.Prior, 0));
                }
                context.SaveChanges();
                var things = context.Frames.Where(f => f.ProjectId == project.Id).ToList();
            }
            return 0;
        }
    }
}
