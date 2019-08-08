using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CommandLine;
using OPS.Imaging;

namespace OPS.LandformUtil
{
    [Verb("limber-dmg", HelpText = "run Limber DMG")]
    public class LimberDMGOptions
    {
        [Value(0, Required = true, HelpText = "image to blend")]
        public string InputImage { get; set; }

        [Value(1, Required = true, HelpText = "index image, should have either one band or same number as input image; valid indices are in 1 - 65534; 0 and 65535 are treated as flags = NO_DATA | HOLD_CONSTANT")]
        public string IndexImage { get; set; }

        [Option(Required = false, HelpText = "flags image (optional), should have either one band or same number as input image; NONE = 0, HOLD_CONSTANT = 1, GRADIENT_ONLY = 2, NO_DATA = 4")]
        public string FlagsImage { get; set; }
        
        [Option(Required = false, HelpText = "color conversion mode", Default = LimberDMG.ColorConversion.RGBToLogLAB)]
        public LimberDMG.ColorConversion ColorConversion { get; set; }

        //NOTE: ResidualEpsilon is 1e-5 in TerrainTools JDBlendImageGradients.cs but defaults to 1e-3 in LimberDMG.cs
        [Option(Required = false, HelpText = "acceptable error in solving the linear system", Default = 1e-5)]
        public double ResidualEpsilon { get; set; }

        [Option(Required = false, HelpText = "number of iterations of Gauss-Seidel relaxation to perform between multigrid iterations", Default = 15)]
        public int NumRelaxationSteps { get; set; }

        [Option(Required = false, HelpText = "higher values will cause sharper transitions between images but better conform to the inputs", Default = 0.003)]
        public double Lambda { get; set; }

        [Option(Required = false, HelpText = "boundary handling: Clamp, WrapSphere, WrapCylinder, WrapTorus", Default = LimberDMG.PoissonProblem2D.EdgeBehavior.Clamp)]
        public LimberDMG.PoissonProblem2D.EdgeBehavior EdgeMode { get; set; }
    }

    public class LimberDMGDriver
    {
        LimberDMGOptions options;
                
        public LimberDMGDriver(LimberDMGOptions options)
        {
            this.options = options;
        }

        public int Run()
        {
            Console.WriteLine("loading input image {0}...", options.InputImage);
            Image composite = Image.Load(options.InputImage);

            Console.WriteLine("loaded {0}x{1} image, {2} bands", composite.Width, composite.Height, composite.Bands);

            Console.WriteLine("loading index image {0}...", options.IndexImage);
            Image index = Image.Load(options.IndexImage, ImageConverters.PassThrough);

            Image flags = null;
            if (!string.IsNullOrEmpty(options.FlagsImage))
            {
                Console.WriteLine("loading flags image {0}...", options.FlagsImage);
                flags = Image.Load(options.FlagsImage, ImageConverters.PassThrough);
            }
            else
            {
                Console.WriteLine("no flags image");
            }

            Console.WriteLine("stitching image with LimberDMG, " +
                              "residual epsilon {0}, num relaxation steps {1}, lambda {2}, edge mode {3}...",
                              options.ResidualEpsilon, options.NumRelaxationSteps, options.Lambda, options.EdgeMode);
            var dmg = new LimberDMG(options.ResidualEpsilon, options.NumRelaxationSteps, options.Lambda,
                                    options.EdgeMode);
            var output = dmg.StitchImage(composite, index, flags, options.ColorConversion);

            var basename = Path.GetFileNameWithoutExtension(options.InputImage);
            var ext = Path.GetExtension(options.InputImage);
            var outFile = basename + "_dmg" + ext;

            Console.WriteLine("saving {0}...", outFile);
            output.Save<byte>(outFile);

            return 0;
        }
    }
}
