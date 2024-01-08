using System;
using System.IO;
using System.Diagnostics;
using CommandLine;
using JPLOPS.Util;
using JPLOPS.Imaging;
using JPLOPS.Geometry;
using JPLOPS.Pipeline.AlignmentServer;

/// <summary>
/// Utility to run LimberDMG on an image.
///
/// LimberDMG is an implementation of composite image stitching to reduce the visibility of seams in an image that is
/// composed of multiple sub-images.  It is used in the Landform blend-images stage (BlendImages.cs) for the contextual
/// mesh workflow.
///
/// Background:
///
/// Example:
///
/// Landform.exe limber-dmg mesh_region_shrink_tex_orbital_adjust.tif mesh_region_shrink_tex_image_numbers.tif
///   --flagsimage mesh_region_shrink_tex_orbital_adjust_limberflags.tif --legacyinvalidindices --outputformat=png
///   --mesh mesh_region_shrink.obj
/// </summary>
namespace JPLOPS.Landform
{
    [Verb("limber-dmg", HelpText = "run Limber DMG")]
    [EnvVar("DMG")]
    public class LimberDMGOptions
    {
        [Value(0, Required = true, HelpText = "image to blend")]
        public string InputImage { get; set; }

        [Value(1, Required = true, HelpText = "index image, should have either one band or same number as input image; valid indices are in 2 - 65535, else treated as flags = NO_DATA | HOLD_CONSTANT")]
        public string IndexImage { get; set; }

        [Option(HelpText = "optional flags image, should have either one band or same number as input image; NONE = 0, HOLD_CONSTANT = 1, GRADIENT_ONLY = 2, NO_DATA = 4")]
        public string FlagsImage { get; set; }

        [Option(HelpText = "output format, e.g. png, jpg, help for list; omit to use input format", Default = null)]
        public string OutputFormat { get; set; }

        [Option(HelpText = "apply output image as texture on this mesh", Default = null)]
        public string Mesh { get; set; }

        [Option(HelpText = "color conversion mode: None, RGBToLAB, RGBToLogLAB", Default = LimberDMG.DEF_COLOR_CONVERSION)]
        public LimberDMG.ColorConversion ColorConversion { get; set; }

        [Option(HelpText = "Disable conversion from/to sRGB for 8 bit image file formats", Default = false)]
        public bool NoSRGBConversion { get; set; }

        [Option(HelpText = "acceptable error in solving the linear system", Default = LimberDMG.DEF_RESIDUAL_EPSILON)]
        public double ResidualEpsilon { get; set; }

        [Option(HelpText = "number of iterations of relaxation to perform between multigrid iterations", Default = LimberDMG.DEF_NUM_RELAXATION_STEPS)]
        public int NumRelaxationSteps { get; set; }

        [Option(HelpText = "number of multigrid iterations to perform", Default = LimberDMG.DEF_NUM_MULTIGRID_ITERATIONS)]
        public int NumMultigridIterations { get; set; }

        [Option(HelpText = "higher values will cause sharper transitions between images but better conform to the inputs", Default = LimberDMG.DEF_LAMBDA)]
        public double Lambda { get; set; }

        [Option(HelpText = "boundary handling: Clamp, WrapSphere, WrapCylinder, WrapTorus", Default = LimberDMG.DEF_EDGE_BEHAVIOR)]
        public LimberDMG.EdgeBehavior EdgeMode { get; set; }

        [Option(HelpText = "include 1 as valid and 65535 as an invalid index", Default = false)]
        public bool LegacyInvalidIndices { get; set; }

        [Option(HelpText = "supplied index image is a backproject index: use only band 0", Default = false)]
        public bool UseBackprojectIndex { get; set; }
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

            var basename = Path.GetFileNameWithoutExtension(options.InputImage);
            var ext = Path.GetExtension(options.InputImage);
            var dir = Path.GetDirectoryName(options.InputImage);

            if (!string.IsNullOrEmpty(options.OutputFormat))
            {
                ext = ImageSerializers.Instance.CheckFormat(options.OutputFormat, Log, Log);
                if (ext == null)
                {
                    return 1;
                }
            }

            if (!string.IsNullOrEmpty(options.Mesh))
            {
                var fmt = Path.GetExtension(options.Mesh).Replace(".", "");
                if (!MeshSerializers.Instance.SupportsFormat(fmt))
                {
                    Log("mesh format \"{0}\" not supported; supported formats: {1}",
                        fmt, string.Join(", ", MeshSerializers.Instance.SupportedFormats()));
                }
            }

            Log("loading input image {0}...", options.InputImage);
            Image composite =
                Image.Load(options.InputImage, options.NoSRGBConversion ? ImageConverters.ValueRangeToNormalizedImage :
                           ImageConverters.ValueRangeSRGBToNormalizedImageLinearRGB);

            Log("loaded {0}x{1} image, {2} bands", composite.Width, composite.Height, composite.Bands);

            Log("loading index image {0}...", options.IndexImage);
            Image index = Image.Load(options.IndexImage, ImageConverters.PassThrough);

            while (options.UseBackprojectIndex && index.Bands > 1)
            {
                index.RemoveBand(index.Bands - 1);
            }

            Image flags = null;
            if (!string.IsNullOrEmpty(options.FlagsImage))
            {
                Log("loading flags image {0}...", options.FlagsImage);
                flags = Image.Load(options.FlagsImage, ImageConverters.PassThrough);
            }
            else
            {
                Log("no flags image");
            }

            Func<int, int, bool> valid =
                (r, c) => index[0, r, c] >= Observation.MIN_INDEX && index[0, r, c] <= Observation.MAX_INDEX;

            if (options.LegacyInvalidIndices)
            {
                valid = (r, c) => index[0, r, c] > 0 && index[0, r, c] < 65535;
            }
            
            Log("stitching image with LimberDMG, " +
                "residual epsilon {0}, {1} relaxation steps, {2} multigrid iterations, lambda {3}, edge mode {4}...",
                options.ResidualEpsilon, options.NumRelaxationSteps, options.NumMultigridIterations,
                options.Lambda, options.EdgeMode);
            var dmg = new LimberDMG(options.ResidualEpsilon, options.NumRelaxationSteps, options.NumMultigridIterations,
                                    options.Lambda, options.EdgeMode, options.ColorConversion,
                                    msg => Log(msg));
            var output = dmg.StitchImage(composite, index, flags, valid);

            var outFile = basename + "_dmg" + ext;
            var outPath = Path.Combine(dir, outFile);
            Log("saving {0}...", outPath);
            output.Save<byte>(outPath, options.NoSRGBConversion ? ImageConverters.NormalizedImageToValueRange :
                              ImageConverters.NormalizedImageLinearRGBToValueRangeSRGB);

            if (!string.IsNullOrEmpty(options.Mesh))
            {
                var meshBase = Path.GetFileNameWithoutExtension(options.Mesh);
                var meshExt = Path.GetExtension(options.Mesh);
                var outMesh = meshBase + "_dmg" + meshExt;
                var meshPath = Path.Combine(dir, outMesh);
                Log("applying {0} as texture on {1}, saving as {2}...", outFile, options.Mesh, meshPath);
                var mesh = Mesh.Load(options.Mesh);
                mesh.Save(meshPath, outFile);
            }

            stopwatch.Stop();
            Log("elapsed time {0}", Fmt.HMS(stopwatch.ElapsedMilliseconds));

            return 0;
        }

        private void Log(string msg, params Object[] args)
        {
            Console.WriteLine(msg, args);
        }
    }
}
