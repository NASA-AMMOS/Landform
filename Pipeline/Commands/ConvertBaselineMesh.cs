using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;
using OPS.Geometry;
using OPS.Imaging;
using System.IO;
using Microsoft.Xna.Framework;

namespace OPS.Pipeline
{
    [Verb("convertbaselinemesh", HelpText = "Converts a basline mesh in open inventor format into something else")]
    public class ConvertBaselineMeshOptions
    {
        [Value(0, Required = true, HelpText = "Filename of baseline mesh")]
        public string InputMesh { get; set; }

        [Value(1, Required = true, HelpText = "Filename of output mesh")]
        public string OutputMesh { get; set; }

        [Option(HelpText = "Input image to use for this mesh")]
        public string InputImage { get; set; }

        [Option(HelpText = "Image output filename")]
        public string OutputImage { get; set; }
    }
    
    public class ConvertBaselineMesh
    {
        ConvertBaselineMeshOptions options;

        public ConvertBaselineMesh(ConvertBaselineMeshOptions options)
        {
            this.options = options;
        }

        public int Run()
        {
            Mesh m = OpenInventorSerializer.ReadBestLOD(options.InputMesh);
            m.Clean();
            string imageName = null;
            if (options.InputImage != null)
            {
                imageName = options.InputImage;
                if (options.OutputImage != null)
                {
                    Image img = Image.Load(options.InputImage);
                    img.Save<byte>(options.OutputImage);
                    imageName = options.OutputImage;
                } 
            }    
            m.Save(options.OutputMesh, imageName);
            return 0;
        }
    }
}
