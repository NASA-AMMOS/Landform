using System;
using CommandLine;


namespace OPS.Pipeline.MeshingWorker
{
    [Verb("MSL.Texture", HelpText = "generate leaf tiles (mesh and texture) for a terrain mesh")]
    public class TextureMeshOptions
    {
        [Value(0, Required = true, HelpText = "Project name for dynamo db")]
        public string ProjectName { get; set; }
    };

    //TODO: rename to backprojectleaves?
    class TextureMesh
    {

        private TextureMeshOptions options;

        public TextureMesh(TextureMeshOptions opts)
        {
            this.options = opts;
        }

        public int Run()
        {
            throw new NotImplementedException("implement the command version of backproject leaves");
        }
    }
}
