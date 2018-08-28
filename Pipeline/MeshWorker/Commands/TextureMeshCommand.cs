using System;
using CommandLine;


namespace OPS.Pipeline.MeshWorker
{
    [Verb("MSL.Texture", HelpText = "generate leaf tiles from a large mesh and textures from image observations")]
    public class TextureMeshOptions
    {
        [Value(0, Required = true, HelpText = "Project name to run")]
        public string ProjectName { get; set; }
    };

    class TextureMeshCommand
    {
        private TextureMeshOptions options;

        public TextureMeshCommand(TextureMeshOptions opts)
        {
            this.options = opts;
        }

        public int Run()
        {
            throw new NotImplementedException();
        }
    }
}
