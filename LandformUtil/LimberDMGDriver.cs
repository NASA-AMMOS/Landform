using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics;
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
        
        [Option(Required = false, HelpText = "color conversion mode: None, RGBToLAB, RGBToLogLAB", Default = LimberDMG.DEF_COLOR_CONVERSION)]
        public LimberDMG.ColorConversion ColorConversion { get; set; }

        [Option(Required = false, HelpText = "acceptable error in solving the linear system", Default = LimberDMG.DEF_RESIDUAL_EPSILON)]
        public double ResidualEpsilon { get; set; }

        [Option(Required = false, HelpText = "number of iterations of relaxation to perform between multigrid iterations", Default = LimberDMG.DEF_NUM_RELAXATION_STEPS)]
        public int NumRelaxationSteps { get; set; }

        [Option(Required = false, HelpText = "number of iterations multigrid iterations to perform", Default = LimberDMG.DEF_NUM_MULTIGRID_ITERATIONS)]
        public int NumMultigridIterations { get; set; }

        [Option(Required = false, HelpText = "higher values will cause sharper transitions between images but better conform to the inputs", Default = LimberDMG.DEF_LAMBDA)]
        public double Lambda { get; set; }

        [Option(Required = false, HelpText = "boundary handling: Clamp, WrapSphere, WrapCylinder, WrapTorus", Default = LimberDMG.DEF_EDGE_BEHAVIOUR)]
        public LimberDMG.EdgeBehavior EdgeMode { get; set; }
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
            var stopwatch = new Stopwatch();
            stopwatch.Start();

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
                              "residual epsilon {0}, {1} relaxation steps, {2} multigrid iterations, " +
                              "lambda {3}, edge mode {4}...",
                              options.ResidualEpsilon, options.NumRelaxationSteps, options.NumMultigridIterations,
                              options.Lambda, options.EdgeMode);
            var dmg = new LimberDMG(options.ResidualEpsilon, options.NumRelaxationSteps, options.NumMultigridIterations,
                                    options.Lambda, options.EdgeMode, options.ColorConversion,
                                    msg => Console.WriteLine(msg));
            var output = dmg.StitchImage(composite, index, flags);

            var basename = Path.GetFileNameWithoutExtension(options.InputImage);
            var ext = Path.GetExtension(options.InputImage);
            var dir = Path.GetDirectoryName(options.InputImage);
            var outFile = Path.Combine(dir, basename + "_dmg" + ext);

            Console.WriteLine("saving {0}...", outFile);
            output.Save<byte>(outFile);

            stopwatch.Stop();

            Console.WriteLine("elapsed time {0:F3}s", 0.001 * stopwatch.ElapsedMilliseconds);

            return 0;
        }
    }
}
