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
        [Option(HelpText = "Option disabled for this command ", Default = 0)]
        public override int DecimateWedgeMeshes { get; set; }

        [Option(HelpText = "Option disabled for this command", Default = 0)]
        public override int DecimateWedgeImages { get; set; }

        [Value(1, Required = false, Default = null, HelpText = "Scene mesh, search project storage if omitted")]
        public string InputMesh { get; set; }

        [Option(HelpText = "Occlusion mesh in same frame as input mesh, defaults to input mesh", Default = null)]
        public string OcclusionMesh { get; set; }

        [Option(HelpText = "Output texture resolution, should be power of two", Default = 4096)]
        public virtual int TextureResolution { get; set; }

        [Option(HelpText = "Observation image texture variant (Original, Blurred, Blended)", Default = Backproject.TextureVariant.Original)]
        public virtual Backproject.TextureVariant TextureVariant { get; set; }

        [Option(HelpText = "Percentage of pixels to test before picking a texture during backprojection", Default = 0.1)]
        public double BackprojectGoodnessSamplingPct { get; set; }

        [Option(Required = false, HelpText = "Blur radius", Default = 7)]
        public int BlurRadius { get; set; }

        [Option(HelpText = "Redo blurred observation textures", Default = false)]
        public bool RedoBlurredObservationTextures { get; set; }
    }

    public class TextureCommand : GeometryCommand
    {
        protected TextureCommandOptions tcopts;

        protected int resolution;

        protected List<Observation> imageObservations;

        protected SceneCaster sceneCaster;

        protected Dictionary<Pixel, Backproject.ObsPixel> backprojectResults;

        protected TextureCommand(TextureCommandOptions tcopts) : base(tcopts)
        {
            this.tcopts = tcopts;
            tcopts.RedoBlurredObservationTextures |= tcopts.Redo;
        }

        protected virtual bool ParseArgumentsAndLoadCaches(string outDir)
        {
            if (tcopts.DecimateWedgeImages < 0 || tcopts.DecimateWedgeImages > 1)
            {
                throw new Exception("--decimatewedgeimages is not implemented for this command");
            }

            if (tcopts.DecimateWedgeMeshes < 0 || tcopts.DecimateWedgeMeshes > 1)
            {
                throw new Exception("--decimatewedgemeshes is not implemented for this command");
            }

            if (!base.ParseArgumentsAndLoadCaches(outDir, new [] { ObservationType.Image, ObservationType.RoverMask },
                                                  onlyObsForReconstruction: true))
            {
                return false; //help
            }

            resolution = tcopts.TextureResolution;
            if (resolution > 0 && (resolution & (resolution - 1)) != 0)
            {
                pipeline.LogWarn("resolution {0} not a power of two", resolution);
            }

            if (observationCache != null)
            {
                string imageObs = ObservationType.Image.ToString();
                imageObservations =
                    observationCache.GetAllObservations().Where(obs => obs.ObservationType == imageObs).ToList();
            }

            return true;
        }

        protected override bool ParseArgumentsAndLoadCaches(string outDir, ObservationType[] obsTypes,
                                                            bool onlyObsForReconstruction)
        {
            throw new NotImplementedException();
        }

        protected void EnsureOrGenerateObservationTextures()
        {
            switch (tcopts.TextureVariant)
            {
                case Backproject.TextureVariant.Original: break;
                case Backproject.TextureVariant.Blurred: GenerateBlurredObservationImages(); break;
                case Backproject.TextureVariant.Blended: EnsureBlendedObservationImages(); break;
                default: throw new Exception("unknown texture variant " + tcopts.TextureVariant);
            }
        }

        protected void EnsureBlendedObservationImages()
        {
            foreach (var obs in imageObservations)
            {
                if (obs.BlendedGuid == Guid.Empty)
                {
                    throw new Exception(string.Format("no blended texture for observation {0}, run local-blend-images"));
                }
            }
        }

        protected void GenerateBlurredObservationImages()
        {
            pipeline.LogInfo("creating blurred observation images");

            int no = imageObservations.Count;
            int np = 0, nc = 0;
            CoreLimitedParallel.ForEach(imageObservations, obs => {

                    if (!tcopts.RedoBlurredObservationTextures && obs.BlurredGuid != Guid.Empty)
                    {
                        Interlocked.Increment(ref nc);
                        return;
                    }

                    Interlocked.Increment(ref np);

                    if (!tcopts.NoProgress)
                    {
                        pipeline.LogInfo("creating blurred image for observation {0}, processing {1} in parallel, " +
                                         "completed {2}/{3}", obs.Name, np, nc, no);
                    }

                    Image img = pipeline.LoadImage(obs.Url);

                    //notes from TerrainTools PDSImageRoutines.cs
                    //"Used to do a guass blur 4 with photoshop"
                    //the current code is: img.SmoothBlur(13, 13)
                    Image blurredImage = img.GaussianBoxBlur(tcopts.BlurRadius);

                    if (tcopts.WriteDebug)
                    {
                        SaveImage(blurredImage, obs.Name + "_blurred");
                    }

                    if (!tcopts.NoSave)
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

        protected void LoadInputMesh(bool requireUVs = true)
        {
            sceneMesh = SceneMesh.Find(pipeline, project.Name, meshFrame);

            if (!string.IsNullOrEmpty(tcopts.InputMesh))
            {
                pipeline.LogInfo("loading input mesh from {0}", tcopts.InputMesh);
                mesh = Mesh.Load(pipeline.GetFileCached(tcopts.InputMesh, "meshes"));
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

            if (requireUVs && !mesh.HasUVs)
            {
                throw new Exception("input mesh needs UVs");
            }

            if (sceneMesh == null)
            {
                sceneMesh = SceneMesh.Create(pipeline, project, meshFrame, siteDrives, MeshVariant.Default, mesh,
                                             noSave: tcopts.NoSave);
            }
        }

        protected void BuildSceneCaster()
        {
            Mesh occlusionMesh = null;
            if (!string.IsNullOrEmpty(tcopts.OcclusionMesh))
            {
                pipeline.LogInfo("loading occlusion mesh {0}", tcopts.OcclusionMesh);
                occlusionMesh = Mesh.Load(pipeline.GetFileCached(tcopts.OcclusionMesh, "meshes"));
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
            backprojectResults = BackprojectObservations(mesh, logging: true, obsToHull: null);
        }

        protected Dictionary<Pixel, Backproject.ObsPixel> BackprojectObservations(Mesh mesh, bool logging, IDictionary<string, ConvexHull> obsToHull)
        {
            return Backproject.BackprojectObservations(pipeline, frameCache, observationCache, mesh, resolution,
                                                       sceneCaster, imageObservations, tcopts.UsePriors,
                                                       tcopts.OnlyAligned, meshFrame, mission,
                                                       tcopts.BackprojectGoodnessSamplingPct, logging, obsToHull);
        }

        protected Image GenerateBackprojectIndex()
        {
            pipeline.LogInfo("creating backproject index");
            Image index = new Image(3, resolution, resolution);
            Backproject.FillIndexImage(backprojectResults, index);

            if (!tcopts.NoSave)
            {
                pipeline.LogInfo("saving backproject index");
                var indexProd = new TiffDataProduct(index);
                pipeline.SaveDataProduct(project, indexProd);
                sceneMesh.BackprojectIndexGuid = indexProd.Guid;
                sceneMesh.Save(pipeline);
            }
            
            if (tcopts.WriteDebug)
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
            Backproject.FillOutputTexture(pipeline, backprojectResults, texture, textureVariant,
                                          fallbackToOriginal: false);

            if (!tcopts.NoSave)
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
            
            if (tcopts.WriteDebug)
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
