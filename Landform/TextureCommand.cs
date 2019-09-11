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
    public class TextureCommandOptions : GeometryCommandOptions
    {
        [Value(1, Required = false, Default = null, HelpText = "Scene mesh, search project storage if omitted")]
        public string InputMesh { get; set; }

        [Option(HelpText = "Occlusion mesh in same frame as input mesh, defaults to input mesh", Default = null)]
        public string OcclusionMesh { get; set; }

        [Option(HelpText = "Backproject texture resolution, should be power of two", Default = 4096)]
        public int TextureResolution { get; set; }

        [Option(HelpText = "Percentage of pixels to test before picking a texture during backprojection", Default = 0.1)]
        public double BackprojectGoodnessSamplingPct { get; set; }

        [Option(Required = false, HelpText = "Blur radius", Default = 7)]
        public int BlurRadius { get; set; }

        [Option(HelpText = "Redo blurred observation textures", Default = false)]
        public bool RedoBlurredObservationTextures { get; set; }
    }

    public class TextureCommand : GeometryCommand
    {
        protected new TextureCommandOptions options;

        protected int resolution;

        protected List<Observation> imageObservations;

        protected SceneCaster sceneCaster;
        protected Dictionary<Pixel, Backproject.ObsPixel> backprojectResults;

        protected TextureCommand(TextureCommandOptions options) : base(options)
        {
            this.options = options;
            options.RedoBlurredObservationTextures |= options.Redo;
        }

        protected virtual bool ParseArgumentsAndLoadCaches(string outDir)
        {
            if (!base.ParseArgumentsAndLoadCaches(outDir, new [] { ObservationType.Image, ObservationType.RoverMask },
                                                  onlyObsForReconstruction: true))
            {
                return false; //help
            }

            resolution = options.TextureResolution;
            if ((resolution & (resolution - 1)) != 0)
            {
                pipeline.LogWarn("resolution {0} not a power of two", resolution);
            }

            string imageObs = ObservationType.Image.ToString();
            imageObservations =
                observationCache.GetAllObservations().Where(obs => obs.ObservationType == imageObs).ToList();

            return true;
        }

        protected void GenerateBlurredObservationImages()
        {
            pipeline.LogInfo("creating blurred observation images");

            int no = imageObservations.Count;
            int np = 0, nc = 0;
            CoreLimitedParallel.ForEach(imageObservations, obs => {

                    if (!options.RedoBlurredObservationTextures && obs.BlurredGuid != Guid.Empty)
                    {
                        Interlocked.Increment(ref nc);
                        return;
                    }

                    Interlocked.Increment(ref np);

                    pipeline.LogInfo("creating blurred image for observation {0}, processing {1} in parallel, " +
                                     "completed {2}/{3}", obs.Name, np, nc, no);

                    Image img = pipeline.LoadImage(obs.Url);

                    //notes from TerrainTools PDSImageRoutines.cs
                    //"Used to do a guass blur 4 with photoshop"
                    //the current code is: img.SmoothBlur(13, 13)
                    Image blurredImage = img.GaussianBoxBlur(options.BlurRadius);

                    if (options.WriteDebug)
                    {
                        SaveImage(blurredImage, obs.Name + "_blurred");
                    }

                    if (!options.NoSave)
                    {
                        var imgProd = new PngDataProduct();
                        pipeline.SaveDataProduct(project, imgProd);
                        obs.BlurredGuid = imgProd.Guid;
                        obs.Save(pipeline);
                    }

                    Interlocked.Decrement(ref np);
                    Interlocked.Increment(ref nc);
                });
        }

        protected void BuildSceneCaster()
        {
            Mesh occlusionMesh = null;
            if (!string.IsNullOrEmpty(options.OcclusionMesh))
            {
                pipeline.LogInfo("loading occlusion mesh {0}", options.OcclusionMesh);
                occlusionMesh = Mesh.Load(pipeline.GetFileCached(options.OcclusionMesh, "meshes"));
                if (occlusionMesh == null)
                {
                    throw new Exception("failed to load occlusion mesh");
                }
                if (occlusionMesh.Faces.Count == 0)
                {
                    throw new Exception("occlusion mesh empty");
                }
            }
            else
            {
                occlusionMesh = mesh;
            }

            pipeline.LogInfo("building occlusion data structures");
            sceneCaster = new SceneCaster();
            sceneCaster.AddMesh(occlusionMesh, null, Matrix.Identity); //NOTE: can't change mesh after adding to collider
            sceneCaster.Build();
        }

        protected void BackprojectObservations()
        {
            pipeline.LogInfo("backprojecting {0} observations", imageObservations.Count);
            backprojectResults =
                Backproject.BackprojectObservations(pipeline, frameCache, observationCache, mesh, resolution,
                                                    sceneCaster, imageObservations, options.UsePriors,
                                                    options.OnlyAligned, meshFrame, mission,
                                                    options.BackprojectGoodnessSamplingPct);
        }

        protected Image GenerateBackprojectIndex()
        {
            pipeline.LogInfo("creating backproject index");
            Image index = new Image(3, resolution, resolution);
            Backproject.FillIndexImage(backprojectResults, index);

            if (!options.NoSave)
            {
                pipeline.LogInfo("saving backproject index");
                var indexProd = new TiffDataProduct(index);
                pipeline.SaveDataProduct(project, indexProd);
                sceneMesh.BackprojectIndexGuid = indexProd.Guid;
                sceneMesh.Save(pipeline);
            }
            
            if (options.WriteDebug)
            {
                pipeline.LogInfo("saving backproject index image and textured mesh");
                string name = sceneMesh.Name + "_backprojectIndex";
                SaveFloatTIFF(index, name);
                Image previewImg = Backproject.GenerateIndexPreviewImage(index);
                name += "FalseColor";
                SaveImage(previewImg, name);
                SaveMesh(mesh, name, name + imageExt);
            }

            return index;
        }

        protected Image GenerateBackprojectTexture(Backproject.TextureVariant textureVariant)
        {
            pipeline.LogInfo("creating backproject texture");
            Image texture = new Image(3, resolution, resolution);
            Backproject.FillOutputTexture(pipeline, backprojectResults, texture, textureVariant);

            if (!options.NoSave)
            {
                pipeline.LogInfo("saving backproject texture");
                var texProd = new PngDataProduct(texture);
                pipeline.SaveDataProduct(project, texProd);
                switch (textureVariant)
                {
                    case Backproject.TextureVariant.Original: sceneMesh.TextureGuid = texProd.Guid; break;
                    case Backproject.TextureVariant.Blurred: sceneMesh.BlurredTextureGuid = texProd.Guid; break;
                    case Backproject.TextureVariant.Blended: sceneMesh.BlendedTextureGuid = texProd.Guid; break;
                    default: throw new Exception("unknown texture variant " + textureVariant);
                }
                sceneMesh.Save(pipeline);
            }
            
            if (options.WriteDebug)
            {
                pipeline.LogInfo("saving backproject texture and textured mesh");
                string name = sceneMesh.Name + "_backprojectTexture_" + textureVariant.ToString();
                SaveImage(texture, name);
                SaveMesh(mesh, name, name + imageExt);
            }

            return texture;
        }
    }
}
