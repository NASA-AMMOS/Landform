using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using JPLOPS.MathExtensions;
using JPLOPS.RayTrace;
using JPLOPS.Geometry;

namespace JPLOPS.Pipeline.Texturing
{
    // a strategy that tests all observations for each pixel
    // will return the highest quality pixel for every output pixel
    // but can create a noisy result by pingponging between textures
    public class ObsSelectionExhaustive : ObsSelectionStrategy
    {
        public override ObsSelectionStrategyName Name { get { return ObsSelectionStrategyName.Exhaustive; } }

        private BoundingBox meshBounds;
        private SceneCaster meshCaster;
        private SceneCaster occlusionScene;

        public override void Initialize(Mesh mesh, MeshOperator meshOp, SceneCaster meshCaster,
                                        SceneCaster occlusionScene, List<Backproject.Context> contexts)
        {
            this.meshBounds = meshOp.Bounds;
            this.meshCaster = meshCaster;
            this.occlusionScene = occlusionScene;
        }

        //this gets called in an inner loop in Backproject and in ObsSelectionSpatial.Initialize()
        //small stuff here can get expensive
        public override List<ScoredContext> FilterAndSortContexts(Vector3 meshPoint, List<Backproject.Context> contexts,
                                                                  SceneCaster meshCaster = null)
        {
            if (contexts == null || contexts.Count == 0)
            {
                return null;
            }

            if (!meshPoint.IsFinite())
            {
                return null; //backproject options sampleTransform (used e.g. by BuildSkySphere) can kill points
            }

            var bestContexts = new BestContexts(this);

            //testing the frustum hull can be a bit expensive
            //InFrame() uses the (possibly nonlinear) camera model and gives a better answer with reasonable perf
            //in the nonlin case the hull can be poorly fitting
            foreach (var ctx in contexts)//.Where(c => c.FrustumHull.Contains(meshPoint)))
            {
                if (Backproject.InFrame(meshPoint, ctx, out Vector2 pixel))
                {
                    //estimate of min meters on mesh per pixel in obs, smaller distance means better texture resolution
                    double dist = ProjectedPixelDistances
                        .CalculateForObs(meshBounds, meshCaster ?? this.meshCaster, occlusionScene,
                                         new List<PixelPoint>() { new PixelPoint(pixel, meshPoint) },
                                         ctx.Obs, ctx.CameraModel, ctx.FrustumHull, ctx.ObsToMesh, RaycastTolerance);
                    if (dist < double.MaxValue && BetterThanOrbital(ctx, dist))
                    {
                        bestContexts.Add(ctx, dist);
                    }
                }
            };

            return bestContexts.GetSortedContexts();
        }

        private bool BetterThanOrbital(Backproject.Context context, double pixelSpread)
        {
            if (OrbitalMetersPerPixel <= 0)
            {
                return true;
            }
            bool obsIsColor = context.Obs.Bands > 1;
            if (PreferColor == PreferColorMode.Always && (obsIsColor != OrbitalIsColor))
            {
                return obsIsColor;
            }
            double diff = Math.Abs(pixelSpread - OrbitalMetersPerPixel);
            double equivAbs = Math.Max(EquivalentScoresAbs, 0);
            double equivRel = Math.Max(EquivalentScoresRel, 0);
            if (diff <= equivAbs || diff <= equivRel * 0.5 * (pixelSpread + OrbitalMetersPerPixel))
            {
                if (PreferSurface)
                {
                    return true;
                }
                if (PreferColor == PreferColorMode.EquivalentScores && (obsIsColor != OrbitalIsColor))
                {
                    return obsIsColor;
                }
            }
            return pixelSpread < OrbitalMetersPerPixel;
        }
    }
}
