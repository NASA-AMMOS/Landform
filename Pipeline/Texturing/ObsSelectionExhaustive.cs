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
        protected SceneCaster OcclusionScene;
        protected MeshOperator MeshOp;
        protected bool WriteDebug;
        protected string LocalOutputPath;
        double OrbitalPixelsPerMeter;

        public override void Initialize(Mesh mesh, MeshOperator meshOp, SceneCaster occlusionScene,
                               List<Backproject.Context> allContexts, int outputTextureResolution, double orbitalMetersPerPixel,
                               double quality, bool writeDebug, string localOutputPath)
        {
            MeshOp = meshOp;
            OcclusionScene = occlusionScene;
            WriteDebug = writeDebug;
            LocalOutputPath = localOutputPath;
            OrbitalPixelsPerMeter = orbitalMetersPerPixel;
        }
    
        public override void FilterAndSortContexts(Vector3 forPoint, List<Backproject.Context> inContexts, List<Backproject.Context> sortedContexts, Dictionary<string, double> scoresByObs)
        {
            sortedContexts.Clear();
            if (scoresByObs != null)
            {
                scoresByObs.Clear();
            }

            //intersecting contexts
            var visibleContexts = inContexts.Where(c => c.FrustumHull.Contains(forPoint));
            
            //calculate goodness: median distance between neighboring source pixels in meters on the terrain
            //smaller distance == better texture resolution
            foreach (var ctx in visibleContexts)
            {
                double dist = double.MaxValue;

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

                     dist = ProjectedPixelDistances.CalculateForObs(OcclusionScene, new List<PixelPoint>() { forSrcPixelPt },
                                                                           ctx.Obs, ctx.CameraModel, ctx.FrustumHull, ctx.ObsToMesh, MeshOp.Bounds,
                                                                           1.0, WriteDebug, LocalOutputPath);

                    //if (dist == double.MaxValue) //TODO: keep?
                    //{
                    //    //if no valid samples, use distance from observation to mesh to have a sortable quality rating
                    //    //  (that's much bigger than per valid inter-pixel distances), otherwise contexts are not really sorted
                    //    Vector3 cameraInOutput = Vector3.Transform(ctx.CameraModel.Unproject(forSrcPixelPt.Pixel).Position, ctx.ObsToMesh);
                    //    Vector3 meshCenter = MeshOp.Bounds.Center();
                    //    dist = Vector3.Distance(meshCenter, cameraInOutput);
                    //}

                    if (dist > OrbitalPixelsPerMeter)
                    {
                        dist = double.MaxValue;
                    }
                }

                //no valid measurement, ignore image
                if (dist != double.MaxValue)
                {
                    scoresByObs.Add(ctx.Obs.Name, dist);
                    sortedContexts.Add(ctx);
                }
            };
            
            //sort contexts by decreasing quality
            sortedContexts.Sort((ctx0, ctx1) => scoresByObs[ctx0.Obs.Name].CompareTo(scoresByObs[ctx1.Obs.Name]));
        }
    }
}
