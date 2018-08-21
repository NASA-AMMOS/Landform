using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using log4net;
using CommandLine;
using Amazon.DynamoDBv2.DataModel;
using OPS.Plumbing;
using OPS.Geometry;
using OPS.Imaging;
using OPS.Pipeline.TileServer;
using OPS.Util;
using OPS.Cloud;

namespace OPS.Pipeline.MeshingWorker
{
    [Verb("MSL.Texture", HelpText = "generate leaf tiles (mesh and texture) for a terrain mesh")]
    public class TextureMeshOptions
    {
        [Value(0, Required = true, HelpText = "Project name for dynamo db")]
        public string ProjectName { get; set; }
    };

    class TextureMesh
    {
        
        private TextureMeshOptions options;

        public TextureMesh(TextureMeshOptions opts)
        {
            this.options = opts;
        }
    }
}
