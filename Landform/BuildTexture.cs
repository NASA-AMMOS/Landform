using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using CommandLine;
using OPS.Util;
using OPS.Imaging;
using OPS.Geometry;
using OPS.Pipeline;

/// <summary>
/// Generate a full-scene texture by backprojecting observation images in a Landform alignment project.
///
/// This is not part of the normal tactical or contextual mesh workflows.
///
/// Also, it is not well tested and is known to be problematic because it requires the full scene mesh to be atlased in
/// build-geometry.  See BuildGeometry.cs for more details.
///    
/// The textured mesh is saved to the location given by the second positional command line parameter, which has similar
/// semantics to the --outputmesh option in build-geometry.
///
/// The backprojected texture is saved as a sibling to the mesh.  The backproject index can also be optionally saved as
/// a float tiff.
///
/// Currently the backproject texture is not saved back to project storage and linked from the SceneMesh.  However, it
/// should be straightforward to do so, and that would enable an optional contextual mesh workflow where backproject is
/// performed on the full scene mesh instead of the individual tiles.  TODO
/// https://github.jpl.nasa.gov/OnSight/Landform/issues/849.
///
/// Example:
///
/// Landform.exe build-texture windjana windjana-textured.ply
///
/// </summary>
namespace OPS.Landform
{
    [Verb("build-texture", HelpText = "backproject a mesh texture and/or index image")]
    public class BuildTextureOptions : TextureCommandOptions
    {
        [Value(1, Required = true, HelpText = "URL, file, or file type (extension starting with \".\") to which to save textured scene mesh")]
        public string OutputMesh { get; set; }

        [Option(HelpText = "Don't generate texture image", Default = false)]
        public bool NoTexture { get; set; }

        [Option(HelpText = "Don't generate index image", Default = false)]
        public bool NoIndex { get; set; }

        [Option(HelpText = "Also save scene index image with output mesh", Default = false)]
        public bool SaveIndex { get; set; }
    }

    public class BuildTexture : TextureCommand
    {
        private const string OUT_DIR = "texturing/TextureProducts";

        private BuildTextureOptions options;

        private Image backprojectTexture;

        public BuildTexture(BuildTextureOptions options) : base(options)
        {
            this.options = options;
        }

        public int Run()
        {
            try
            {
                if (!ParseArgumentsAndLoadCaches())
                {
                    return 0; //help
                }

                RunPhase(string.Format("checking/generating {0} observation textures", options.TextureVariant),
                         EnsureOrBuildObservationTextures);

                RunPhase("loading input mesh", () => LoadInputMesh(requireUVs: true));
                RunPhase("build occlusion datastructures", BuildSceneCaster);
                RunPhase("build acceleration datastructures", BuildMeshOperator);
                RunPhase("backproject observations", BackprojectRoverObservations);

                if (!options.NoOrbital)
                {
                    RunPhase("backproject orbital", BackprojectOrbital);
                }

                if (!options.NoIndex)
                {
                    RunPhase("generate backproject index", BuildBackprojectIndex);
                }

                if (!options.NoTexture)
                {
                    RunPhase("checking/generating observation image masks", BuildObservationImageMasks);
                    RunPhase(string.Format("generate {0} backproject texture", options.TextureVariant),
                             () => { backprojectTexture = BuildBackprojectTexture(options.TextureVariant); });
                }

                if (!options.NoSave)
                {
                    RunPhase("save mesh", SaveMesh);
                }
            }
            catch (Exception ex)
            {
                pipeline.LogException(ex);
                return 1;
            }

            StopStopwatch();

            return 0;
        }

        private bool ParseArgumentsAndLoadCaches()
        {
            if (options.NoIndex && options.NoTexture)
            {
                throw new Exception("cannot specify both --noindex and --notexture");
            }

            return base.ParseArgumentsAndLoadCaches(OUT_DIR);
        }

        protected override bool ParseArgumentsAndLoadCaches(string outDir)
        {
            throw new NotImplementedException();
        }

        private void SaveMesh()
        {
            var meshURL = CheckOutputURL(options.OutputMesh, sceneMesh.Name, OUT_DIR, MeshSerializers.Instance);
            var imgURL = StringHelper.ChangeUrlExtension(meshURL, imageExt);

            if (options.SaveIndex && backprojectIndex != null)
            {
                var ext = ".tif";
                var indexURL = StringHelper.ChangeUrlExtension(meshURL, ext);
                pipeline.LogInfo("saving {0}x{1} float tiff backproject index image {2}",
                                 backprojectIndex.Width, backprojectIndex.Height, indexURL);
                TemporaryFile.GetAndDelete(ext, tmpFile =>
                {
                    var opts = new GDALTIFFWriteOptions(GDALTIFFWriteOptions.CompressionType.DEFLATE);
                    var serializer = new GDALSerializer(opts);
                    serializer.Write<float>(tmpFile, backprojectIndex);
                    pipeline.SaveFile(tmpFile, indexURL, constrainToStorage: false);
                });
            }

            if (backprojectTexture != null)
            {
                pipeline.LogInfo("saving {0}x{1} backproject texture {2}",
                                 backprojectTexture.Width, backprojectTexture.Height, imgURL);
                TemporaryFile.GetAndDelete(imageExt, tmpFile =>
                {
                    backprojectTexture.Save<byte>(tmpFile);
                    pipeline.SaveFile(tmpFile, imgURL, constrainToStorage: false);
                });
            }

            if (mesh != null)
            {
                pipeline.LogInfo("saving {0}scene mesh", backprojectTexture != null ? "textured " : "");
                TemporaryFile.GetAndDelete(StringHelper.GetUrlExtension(meshURL), tmpFile =>
                {
                    string texture = backprojectTexture != null ? StringHelper.GetLastUrlPathSegment(imgURL) : null;
                    mesh.Save(tmpFile, texture);
                    pipeline.SaveFile(tmpFile, meshURL, constrainToStorage: false);
                });
            }
        }
    }
}
