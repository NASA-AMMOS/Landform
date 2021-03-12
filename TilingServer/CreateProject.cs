using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using Amazon.DynamoDBv2.Model;
using CommandLine;
using log4net;
using OPS.Util;
using OPS.Geometry;
using OPS.Imaging;
using OPS.Pipeline;
using OPS.Pipeline.TilingServer;

namespace OPS.TilingServer
{
    [Verb("createproject", HelpText = "creates a project")]
    public class CreateProjectOptions : TilingServerCommandOptions
    {
        [Option(Default = ProjectType.GenericTiling, HelpText = "processing pipline, currently only GenericTiling is supported")]
        public ProjectType ProjectType { get; set; }

        [Option(Default = TilingDefaults.TILING_SCHEME, HelpText = "tiling scheme (Bin, QuadX, QuadY, QuadZ, QuadAuto, Oct, UserDefined, Flat)")]
        public TilingScheme TilingScheme { get; set; }

        [Option(Default = TilingDefaults.MAX_FACES_PER_TILE, HelpText = "maximum faces per tile")]
        public int MaxFacesPerTile { get; set; }

        [Option(Default = TilingDefaults.MIN_TILE_EXTENT, HelpText = "minimum tile bounds extent")]
        public double MinTileExtent { get; set; }

        [Option(Default = TilingDefaults.MAX_LEAF_AREA, HelpText = "maximum leaf mesh area")]
        public double MaxLeafArea { get; set; }

        [Option(Default = TilingDefaults.PARENT_RECONSTRUCTION_METHOD, HelpText = "parent tile mesh reconstruction method")]
        public MeshReconstructionMethod ParentReconstructionMethod { get; set; }

        [Option(Default = TilingDefaults.SKIRT_MODE, HelpText = "skirt mode")]
        public SkirtMode SkirtMode { get; set; }

        [Option(Default = TilingDefaults.TEXTURE_MODE, HelpText = "texture mode (None, Clip, Bake)")]
        public TextureMode TextureMode { get; set; }

        [Option(Default = TilingDefaults.MAX_TILE_RESOLUTION, HelpText = "maximum image resolution per tile, 0 disables texturing, negative for unlimited/default")]
        public int MaxTextureResolution { get; set; }

        [Option(Default = TilingDefaults.MAX_TEXELS_PER_METER, HelpText = "Max texels per meter (lineal not areal), 0 or negative for unlimited")]
        public double MaxTexelsPerMeter { get; set; }

        [Option(Default = TilingDefaults.MAX_TEXTURE_STRETCH, HelpText = "Max texture atlas stretch (0 = no stretch, 1 = unlimited)")]
        public double MaxTextureStretch { get; set; }

        [Option(HelpText = "Require power of two textures", Default = TilingDefaults.POWER_OF_TWO_TEXTURES)]
        public bool PowerOfTwoTextures { get; set; }

        [Option(HelpText = "Don't convert tileset images from linear RGB to sRGB", Default = !TilingDefaults.CONVERT_LINEAR_RGB_TO_SRGB)]
        public bool NoConvertLinerRGBToSRGB { get; set; }

        [Option(HelpText = "Write out index images as seperate files", Default = TilingDefaults.EMBED_INDEX_IMAGES)]
        public bool EmbedIndexImages { get; set; }

        [Option(Default = null, HelpText = "write additional mesh format, or \"help\" to list")]
        public string ExportMeshFormat { get; set; }

        [Option(Default = null, HelpText = "write additional image format, or \"help\" to list")]
        public string ExportImageFormat { get; set; }

        [Option(Default = false, HelpText = "do not wait until project has been created")]
        public bool NoWait { get; set; }
    }

    public class CreateProject : TilingServerCommand
    {
        const int MAX_WAIT_MS = 60 * 1000;
        const int SLEEP_MS = 500;

        private CreateProjectOptions options;

        public PipelineCore GetPipeline()
        {
            return pipeline;
        }

        public CreateProject(CreateProjectOptions options) : base(options, ExecutionMode.Immediate)
        {
            this.options = options;
        }

        public int Run()
        {
            try
            {
                if (options.ProjectType != ProjectType.GenericTiling)
                {
                    pipeline.LogError("unsupported project type: {0}, currently only {1} is supported",
                                      options.ProjectType, ProjectType.GenericTiling);
                    return 1;
                }

                string exMeshFmt = null;
                if (!string.IsNullOrEmpty(options.ExportMeshFormat))
                {
                    exMeshFmt = options.ExportMeshFormat.ToLower();

                    if (exMeshFmt == "help")
                    {
                        //print as error so that this will get forwarded back to REST API response
                        pipeline.LogError("valid mesh export formats: {0}",
                                          String.Join(", ", MeshSerializers.Instance.SupportedFormats()));
                        return 1; //not really an error, but can't return success status either
                    }

                    if (!MeshSerializers.Instance.SupportsFormat(exMeshFmt))
                    {
                        pipeline.LogError("cannot create project \"{0}\", invalid mesh export format \"{1}\", " +
                                          "valid formats: {2}", options.ProjectName, options.ExportMeshFormat,
                                          String.Join(", ", MeshSerializers.Instance.SupportedFormats()));
                        return 1; //argument error
                    }
                }

                string exImageFmt = null;
                if (!string.IsNullOrEmpty(options.ExportImageFormat))
                {
                    exImageFmt = options.ExportImageFormat.ToLower();

                    //TODO this is a workaround for
                    //https://github.jpl.nasa.gov/OnSight/Landform/issues/347
                    string[] fmts = new string[]
                    {
                        "img", "vic", "lbl", "dds", "crn",
                        "tif", "tiff", "jpg", "bmp", "png",
                        "jp2", "j2k", "fit", "fits", "rgb"
                    };

                    if (exImageFmt == "help")
                    {
                        //print as error so that this will get forwarded back to REST API response
                        pipeline.LogError("valid image export formats: {0}",
                                          String.Join(", ", fmts /* ImageSerializers.Instance.SupportedFormats() */));
                        return 1; //not really an error, but can't return success status either
                    }

                    if (Array.IndexOf(fmts, exImageFmt) < 0 /* !ImageSerializers.Instance.SupportsFormat(exImageFmt) */)
                    {
                        pipeline.LogError("cannot create project \"{0}\", invalid image export format \"{1}\", " +
                                          "valid formats: {2}", options.ProjectName, options.ExportImageFormat,
                                          String.Join(", ", fmts /* ImageSerializers.Instance.SupportedFormats() */));
                        return 1; //argument error
                    }
                }

                var project = TilingProject.Find(pipeline, options.ProjectName);
                if (project != null)
                {
                    pipeline.LogError("project \"{0}\" already exists", options.ProjectName);
                    return 1; //argument error
                }

                string productUrl =
                    pipeline.GetStorageUrl(InitializeAlignmentProject.DATA_PRODUCT_DIR, options.ProjectName);

                pipeline.EnqueueToMaster(new CreateProjectMessage(options.ProjectName)
                {
                    ProjectType = options.ProjectType,
                        ProductPath = productUrl,

                        TilingScheme = options.TilingScheme,
                        MaxFacesPerTile = options.MaxFacesPerTile,
                        MinTileExtent = options.MinTileExtent,
                        MaxLeafArea = options.MaxLeafArea,
                        ParentReconstructionMethod = options.ParentReconstructionMethod,
                        SkirtMode = options.SkirtMode,
                                             
                        TextureMode = options.TextureMode,
                        MaxTextureResolution = options.MaxTextureResolution,
                        MaxTexelsPerMeter = options.MaxTexelsPerMeter,
                        MaxTextureStretch = options.MaxTextureStretch,
                        PowerOfTwoTextures = options.PowerOfTwoTextures,
                        ConvertLinearRGBToSRGB = !options.NoConvertLinerRGBToSRGB,

                        EmbedIndexImages = options.EmbedIndexImages,

                        ExportMeshFormat = exMeshFmt,
                        ExportImageFormat = exImageFmt
                        });

                if (!options.NoWait)
                {
                    pipeline.LogInfo("waiting for project \"{0}\" to be created", options.ProjectName);
                    var sw = new Stopwatch();
                    sw.Start();
                    do
                    {
                        if (sw.ElapsedMilliseconds > MAX_WAIT_MS)
                        {
                            pipeline.LogError("project \"{0}\" not created in {1}",
                                              options.ProjectName, Fmt.HMS(MAX_WAIT_MS));
                            return 2; //internal error
                        }
                        Thread.Sleep(SLEEP_MS);
                        project = TilingProject.Find(pipeline, options.ProjectName);
                    }
                    while (project == null);

                    pipeline.LogInfo("project \"{0}\" created", options.ProjectName);
                }
            }
            catch (Exception ex)
            {
                pipeline.LogException(ex);
                return 1;
            }
            return 0;
        }

        public void DeleteIfExists()
        {
            var tilingProject = TilingProject.Find(pipeline, options.ProjectName);
            if (tilingProject != null)
            {
                pipeline.LogInfo("deleting existing tiling project \"{0}\"", options.ProjectName);
                tilingProject.Delete(pipeline, false);
            }
        }
    }  
}
