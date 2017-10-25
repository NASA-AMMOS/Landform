using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using OPS.Cloud;
using CommandLine;
using Amazon.StepFunctions;
using Amazon.StepFunctions.Model;
using System.Threading;
using Amazon.S3;
using Amazon.S3.Model;
using System.IO;
using OPS.Geometry;
using OPS.Imaging;
using OPS.Util;


/// <summary>
/// This file is just a holder for things I want to try using AWSSDK :) 
/// command line: client --awsprofile default 
/// </summary>

namespace OPS.Pipeline
{
    [Verb("client", HelpText = "trying things out")]
    public class ClientOptions
    {
        [Option(Required = false, HelpText = "Name of the aws profile to use to authenticate")]
        public string AwsProfile { get; set; }
    }

    public class Client
    {
        private readonly string[] EXTENSIONS = new string[] { ".obj", ".mtl", ".jpg" };
        private const int OBJ = 0; private const int MTL = 1; private const int IMG = 2; //indices of file types in extension array 
        private readonly int[] INDICES = new int[] { 0, 1, 2, 3 };

        private const string PROJECT = "pasta";
        private const string FRAME = "spagetti";
        private const string OBSERVATION = "lasangia"; 

        public ClientOptions options;

        public Client(ClientOptions options)
        {
            this.options = options;
        }

        public int Run()
        {
            //load and resize an image 
            Image im = Image.Load(@"C:\Users\gpease\Documents\image\test\rings_sm_orig.gif");
            var newim = im.ResizeSimpleBicubic(800,800);
            newim.Save<byte>(@"C:\Users\gpease\Documents\image\test\old.jpg");

            newim = im.ResizeSimpleBicubic(150, 150);
            newim.Save<byte>(@"C:\Users\gpease\Documents\image\test\oldshrink.jpg");

            //var wideim = im.Resize(800, 800);
            //ideim.Save<byte>(@"C:\Users\gpease\Documents\image\test\bigrings.jpg");

            //var small = im.Resize(150, 150);
            //small.Save<byte>(@"C:\Users\gpease\Documents\image\test\smallrings.jpg");

            //im.GuassianBoxBlur(1);
            //small = im.Shrink(200, 200);
            //small.Save<byte>(@"C:\Users\gpease\Documents\image\test\refactor\smallwithblur.jpg");

            //trying logging to windows application event log 
            /*
            string sSource = "Landform";
            string sLog = "Application";
            string sEvent = "Sample Event";

            if (!EventLog.Exists(sSource))
            {
                EventLog.CreateEventSource(sSource, sLog);
            }

            //loooooooooooooog
            EventLog.WriteEntry(sSource, sEvent);
            //other logging option 
            EventLog.WriteEntry(sSource, sEvent, EventLogEntryType.Warning); 

            /*
            //old connection string landformtest-cluster.cluster-cr2odjbwyrq6.us-west-1.rds.amazonaws.com
            LandformDatabase db = new LandformDatabase();

            LandformDbContext context = db.CreateContext();

            Project p = Project.Find(context, PROJECT);

            //display all projects in the database 
            var query = from proj in context.Projects
                        orderby proj.Name
                        select proj;



            Console.WriteLine("all projects in db");
            foreach (var item in query)
            {
                Console.WriteLine(item.Name);
            }
            /*
            Project p = Project.Create(context, PROJECT);

            Frame f = Frame.Create(context, p, FRAME);

            Observation.Create(context, f, OBSERVATION, "I'm a url", "I'm an obs type", "I'm a camera model", false);
            */
            return 0;

        }
    }
}