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


        public override void Initialize(Mesh mesh, MeshOperator meshOp, SceneCaster occlusionScene, 
                               List<Backproject.Context> allContexts, int outputTextureResolution, double quality,
                               bool writeDebug, string localOutputPath)
        {
            MeshOp = meshOp;
            OcclusionScene = occlusionScene;          
            WriteDebug = writeDebug;
            LocalOutputPath = localOutputPath;

    }

    public override void FilterAndSortContexts(Vector3 forPoint, List<Backproject.Context> inContexts, List<Backproject.Context> sortedContexts, Dictionary<string, double> scoresByObs)
        {
            //intersecting contexts
            var visibleContexts = inContexts.Where(c => c.FrustumHull.Contains(forPoint));

            //calculate goodness: median distance between neighboring source pixels in meters on the terrain
            //smaller distance == better texture resolution
            //var localScoresByObs = new ConcurrentDictionary<string, double>()
            foreach(var ctx in visibleContexts)
            {
                CameraModel cam = (CameraModel)JsonHelper.FromJson(ctx.Obs.CameraModel);
                PixelPoint forSrcPixelPt = new PixelPoint
                {
                    Pixel = cam.Project(Vector3.Transform(forPoint, ctx.MeshToObs), out double range),
                    Point = forPoint
                };

                double dist = ProjectedPixelDistances.CalculateForObs(OcclusionScene, new List<PixelPoint>() { forSrcPixelPt },
                                                                       ctx.Obs, ctx.FrustumHull, ctx.ObsToMesh, 1.0, WriteDebug, LocalOutputPath);
                if(dist == double.MaxValue)
                {
                    //if no valid samples, use distance from observation to mesh to have a sortable quality rating
                    //  (that's much bigger than per valid inter-pixel distances), otherwise contexts are not really sorted
                    Vector3 cameraInOutput = Vector3.Transform(cam.Unproject(forSrcPixelPt.Pixel).Position, ctx.ObsToMesh);  
                    Vector3 meshCenter = MeshOp.Bounds.Center();
                    dist = Vector3.Distance(meshCenter, cameraInOutput);
                }
                scoresByObs.Add(ctx.Obs.Name, dist);
            };

            //sort contexts by decreasing quality
            sortedContexts.Clear();
            foreach (var ctx in visibleContexts)
            {
                sortedContexts.Add(ctx);
            }

            sortedContexts.Sort((ctx0, ctx1) => scoresByObs[ctx0.Obs.Name].CompareTo(scoresByObs[ctx1.Obs.Name]));
        }
    }
}
