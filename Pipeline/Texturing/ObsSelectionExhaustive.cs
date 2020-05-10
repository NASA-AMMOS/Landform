using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Geometry;
using OPS.Pipeline.AlignmentServer;
using OPS.RayTrace;
using OPS.Util;
using Microsoft.Xna.Framework;
using OPS.Imaging;
using System.IO;

namespace OPS.Pipeline.Texturing
{
    // a strategy that tests all observations for each pixel
    // will return the highest quality pixel for every output
    // pixel but can create a noisy result by pingponging
    // between similar quality textures
    public class ObsSelectionExhaustive : ObsSelectionStrategy
    {
        public override ObsSelectionStrategyName Name { get { return ObsSelectionStrategyName.Exhaustive; } }

        private SceneCaster occlusionScene;
        private MeshOperator meshOp;
        private bool preferColor;

        public override void Initialize(Mesh mesh, MeshOperator meshOp, SceneCaster occlusionScene,
                                        List<Backproject.Context> contexts, int outputTextureResolution,
                                        double quality = 1, bool preferColor = true)
        {
            this.meshOp = meshOp;
            this.occlusionScene = occlusionScene;
            this.preferColor = preferColor;
        }
    
        public override List<Backproject.Context> FilterAndSortContexts(Vector3 forPoint,
                                                                        List<Backproject.Context> contexts,
                                                                        Dictionary<string, double> scoresByObs = null)
        {
            var sortedContexts = new List<Backproject.Context>();

            if (scoresByObs != null)
            {
                scoresByObs.Clear();
            }

            //intersecting contexts
            var visibleContexts = contexts.Where(c => c.FrustumHull.Contains(forPoint));
            
            //calculate goodness: median distance between neighboring source pixels in meters on the terrain
            //smaller distance == better texture resolution
            foreach (var ctx in visibleContexts)
            {
                double dist = double.MaxValue; //estimate of min meters on mesh per pixel in obs

                var pixel = ctx.CameraModel.Project(Vector3.Transform(forPoint, ctx.MeshToObs), out double range);

                //frustum hull in nonlinear case is poorly fitting, points can be in hull that are not in image
                // these cause the camera model to break down so we can't do unproject with thouse pixels
                if (pixel.X < 0 || pixel.X >= ctx.Obs.Width || pixel.Y < 0 || pixel.Y >= ctx.Obs.Height)
                {
                    dist = double.MaxValue;
                }
                else
                {
                    PixelPoint forSrcPixelPt = new PixelPoint
                    {
                        Pixel = pixel,
                        Point = forPoint
                    };

                     dist = ProjectedPixelDistances.CalculateForObs(occlusionScene,
                                                                    new List<PixelPoint>() { forSrcPixelPt },
                                                                    ctx.Obs, ctx.CameraModel, ctx.FrustumHull,
                                                                    ctx.ObsToMesh, meshOp.Bounds);
                     
                    if (OrbitalMetersPerPixel > 0 && dist > OrbitalMetersPerPixel)
                    {
                        dist = double.MaxValue;
                    }
                }

                if (dist != double.MaxValue)
                {
                    if (scoresByObs != null)
                    {
                        scoresByObs.Add(ctx.Obs.Name, dist);
                    }
                    sortedContexts.Add(ctx);
                }
            };
            
            //sort contexts by decreasing quality
            //highest quality is min meters on mesh per pixel in obs, so sort in order low to high
            sortedContexts.Sort((ctx0, ctx1) => scoresByObs[ctx0.Obs.Name].CompareTo(scoresByObs[ctx1.Obs.Name]));

            if (preferColor)
            {
                sortedContexts = sortedContexts.OrderByDescending(ctx => ctx.Obs.Bands).ToList();
            }

            return MaxContexts > 0 ? sortedContexts.Take(MaxContexts).ToList() : sortedContexts.ToList();
        }
    }
}
