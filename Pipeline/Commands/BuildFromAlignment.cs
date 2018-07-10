using CommandLine;
using log4net;
using OPS.Alignment;
using OPS.Cloud;
using OPS.Plumbing;
using System;
using System.Collections.Generic;
using OPS.Imaging;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using OPS.Geometry;

namespace OPS.Pipeline
{
    [Verb("buildfromalignment", HelpText = "Reads an alignment solution in the database and builds a pointcloud")]
    public class BuildFromAlignmentOptions
    {
        [Value(0, Required = true, HelpText = "Name of project to use")]
        public string ProjectName { get; set; }

        [Value(3, Required = true, HelpText = "Prefix for database")]
        public string DynamoDBPrefix { get; set; }
    }


    public class BuildFromAlignment : PipelineCore
    {
        BuildFromAlignmentOptions options;

        static ILog logger = LogManager.GetLogger(typeof(BuildFromAlignment));
        const int MIN_MATCHES = 20;
        public static ASIFTDetector detector = new ASIFTDetector(maxSimulatedDimension: 1024);

        public BuildFromAlignment(BuildFromAlignmentOptions options) : base(dynamoPrefix: options.DynamoDBPrefix)
        {
            this.AddProfile("s3://landlords-dev/", "landlords");
            this.AddProfile("s3://red-product/", "mslice");
            this.options = options;
        }

        public int Run()
        {
            var pointObs = Observation.FindByType(DynamoContext, options.ProjectName, "Points").ToArray();
            List<Frame> frames = new List<Frame>();
            Mesh mesh = new Mesh();
            Frame root = null;
            foreach (var ob in pointObs)
            {
                if(!ob.UseForReconstruction)
                {
                    Console.WriteLine("Skipping observation!");
                    continue;
                }
                S3ImageRef s3ref = new S3ImageRef(ob.Url);
                Image img = this.Load(s3ref, true);
                Frame frame = frames.Find(new Predicate<Frame>((f) => f.Name == ob.FrameName));
                if (frame == null)
                {
                    frame = Frame.Find(DynamoContext, options.ProjectName, ob.FrameName);
                    frames.Add(frame);
                }

                List<Vector3> origins = new List<Vector3>();
                List<Vector3> rays = new List<Vector3>();
                for (int r = 0; r < img.Height; r += 50)
                {
                    for (int c = 0; c < img.Width; c += 50)
                    {
                        var ray = img.CameraModel.Unproject(new Vector2(r, c));
                        origins.Add(ray.Position);
                        rays.Add(ray.Direction * img[0, r, c]);
                    }
                }

                while (frame != null)
                {               
                    UncertainRigidTransform transform =  FrameTransform.Find(DynamoContext, frame).Transform;
                    rays = rays.Select((p) => { return transform.TransformPoint(p).XnaMean; }).ToList();
                    frame = frame.GetParent(DynamoContext);
                }

                /*if(root == null)
                {
                    root = frame;
                }*/
                Console.WriteLine("Testing Frame!");
                /*if(root.Name != frame.Name)
                {
                    Console.WriteLine("This is not good....");
                }*/

                for(int i = 0; i < origins.Count; i++)
                {
                    mesh.Vertices.Add(new Vertex(origins[i] + rays[i]));
                }      
            }

            mesh.Save("C:\\Users\\conductor\\Desktop\\aligned-clouds\\test2.ply");

            return 0;
        }
    }
}
