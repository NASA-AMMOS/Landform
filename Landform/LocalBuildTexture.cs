using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using CommandLine;
using Microsoft.Xna.Framework;
using OPS.Util;
using OPS.Imaging;
using OPS.RayTrace;
using OPS.Geometry;
using OPS.Pipeline;
using OPS.Pipeline.AlignmentServer;
using OPS.Pipeline.TilingServer;

namespace OPS.Landform
{
    [Verb("local-build-texture", HelpText = "backproject a mesh texture and/or index image")]
    public class LocalBuildTextureOptions : TextureCommandOptions
    {
        [Option(HelpText = "Don't generate texture image", Default = false)]
        public bool NoTexture { get; set; }

        [Option(HelpText = "Don't generate index image", Default = false)]
        public bool NoIndex { get; set; }

        [Option(HelpText = "Texture variant (Original, Blurred, Blended)", Default = Backproject.TextureVariant.Original)]
        public Backproject.TextureVariant TextureVariant { get; set; }
    }

    public class LocalBuildTexture : TextureCommand
    {
        protected new LocalBuildTextureOptions options;

        public LocalBuildTexture(LocalBuildTextureOptions options) : base(options)
        {
            this.options = options;
        }

        public int Run()
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();

            try
            {
                if (!ParseArgumentsAndLoadCaches("meshing/TextureProducts"))
                {
                    return 0; //help
                }
            }
            catch (Exception ex)
            {
                pipeline.LogError(ex.Message);
                return 1;
            }

            string what = "";
            try
            {
                what = "observation textures";
                EnsureOrGenerateObservationTextures();

                what = "input mesh";
                LoadInputMesh();

                what = "occlusion datastructures";
                BuildSceneCaster();
                
                what = "backprojection";
                BackprojectObservations();

                if (!options.NoIndex)
                {
                    what = "backproject index";
                    GenerateBackprojectIndex();
                }

                if (!options.NoTexture)
                {
                    what = "backproject texture";
                    GenerateBackprojectTexture(options.TextureVariant);
                }
            }
            catch (Exception ex)
            {
                pipeline.LogError("failed to load or generate {0}: {1}", what, ex.Message);
                return 1;
            }

            stopwatch.Stop();
            pipeline.LogInfo("elapsed time {0:F3}s", 0.001 * stopwatch.ElapsedMilliseconds);

            return 0;
        }

        private void EnsureOrGenerateObservationTextures()
        {
            switch (options.TextureVariant)
            {
                case Backproject.TextureVariant.Original: break;
                case Backproject.TextureVariant.Blurred: GenerateBlurredObservationImages(); break;
                case Backproject.TextureVariant.Blended: EnsureBlendedObservationImages(); break;
                default: throw new Exception("unknown texture variant " + options.TextureVariant);
            }
        }

        private void EnsureBlendedObservationImages()
        {
            foreach (var obs in imageObservations)
            {
                if (obs.BlendedGuid == Guid.Empty)
                {
                    throw new Exception(string.Format("no blended texture for observation {0}, run local-blend-images"));
                }
            }
        }

        protected override bool ParseArgumentsAndLoadCaches(string outDir)
        {
            if (options.NoIndex && options.NoTexture)
            {
                throw new Exception("cannot specify both --noindex and --notexture");
            }

            if (options.DecimateWedgeImages < 0 || options.DecimateWedgeImages > 1)
            {
                throw new Exception("--decimatewedgeimages is not implemented for this command");
            }

            return base.ParseArgumentsAndLoadCaches(outDir);
        }

        private void LoadInputMesh()
        {
            sceneMesh = SceneMesh.Find(pipeline, project.Name, meshFrame);

            if (!string.IsNullOrEmpty(options.InputMesh))
            {
                pipeline.LogInfo("loading input mesh from {0}", options.InputMesh);
                mesh = Mesh.Load(pipeline.GetFileCached(options.InputMesh, "meshes"));
            }
            else if (sceneMesh != null)
            {
                if (sceneMesh.MeshGuid != Guid.Empty)
                {
                    pipeline.LogInfo("loading scene mesh in frame {0} from database", meshFrame);
                    mesh = pipeline.GetDataProduct<PlyGZDataProduct>(project, sceneMesh.MeshGuid).Mesh;
                }
                else
                {
                    throw new Exception("scene mesh in database but without mesh");
                }
            }
            else
            {
                throw new Exception("no input mesh specified and no scene mesh in database");
            }

            if (mesh == null)
            {
                throw new Exception("failed to load input mesh");
            }

            if (mesh.Faces.Count == 0)
            {
                throw new Exception("input mesh empty");
            }

            if (!mesh.HasUVs)
            {
                throw new Exception("input mesh needs UVs");
            }

            if (sceneMesh == null)
            {
                sceneMesh = SceneMesh.Create(pipeline, project, meshFrame, siteDrives, MeshVariant.Default, mesh,
                                             noSave: options.NoSave);
            }
        }
    }
}
