using log4net;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Util;
using OPS.Imaging;
using OPS.Util;

namespace OPS.Geometry
{
    public static class DemOperations
    {
        static ILog logger = LogManager.GetLogger(typeof(DemOperations));

        //Swap x, y and negate z
        public static Matrix demToSitedriveCoordinateFlip = new Matrix(0, 1, 0, 0,
                                                                       1, 0, 0, 0,
                                                                       0, 0, -1, 0,
                                                                       0, 0, 0, 1);

        /// <summary>
        /// Do bilinear interpolation with potentially null points. x and y should be horizontal and vertical offset from the top left corner respectively, see diagram.
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <param name="tl"></param>
        /// <param name="tr"></param>
        /// <param name="bl"></param>
        /// <param name="br"></param>
        /// <returns></returns>
        /// 
        /// tl ---------------------------- tr
        ///  |       |                      |
        ///  |       | y                    |
        ///  |       |                      |
        ///  |-------P                      |
        ///  |   x                          |
        ///  |                              |
        ///  |                              |
        ///  |                              |
        ///  |                              |
        ///  |                              |
        /// bl ---------------------------- br
        /// 
        ///  Returns the interpolated value at point P by summing the values at each corner, weighted by the area to the far corner.
        ///  If a corner is missing, its contribution is ignored in the weighted average.

        public static Vector3? Interpolate(double x, double y, Vector3? tl, Vector3? tr, Vector3? bl, Vector3? br)
        {
            Vector3 ret = new Vector3(0, 0, 0);
            double area = 0;
            if (tl.HasValue)
            {
                ret += tl.Value * (1-x) * (1-y);
                area += (1-x) * (1-y);
            }
            if (tr.HasValue)
            {
                ret += tr.Value * x * (1-y);
                area += x * (1-y);
            }
            if (bl.HasValue)
            {
                ret += bl.Value * (1 - x) * y;
                area += (1 - x) * y;
            }
            if (br.HasValue)
            {
                ret += br.Value * x * y;
                area += x * y;
            }
            if (area > 0)
            {
                return ret / area;
            }
            else
            {
                return null;
            }
        }

        public static Vector3? GetInterpolatedXYZ(Image dem, double r, double c, double minFilter, double maxFilter = 1000000)
        {
            //r -= 0.5; //Correct for half pixel offset in projection
            //c -= 0.5;
            Vector3? tl = GetXYZ(dem, (int)r, (int)c);
            Vector3? tr = GetXYZ(dem, (int)r, (int)Math.Ceiling(c));
            Vector3? bl = GetXYZ(dem, (int)Math.Ceiling(r), (int)c);
            Vector3? br = GetXYZ(dem, (int)Math.Ceiling(r), (int)Math.Ceiling(c));
            return Interpolate(c - (int)c, r - (int)r, tl, tr, bl, br);
        }

        /// <summary>
        /// Unprojects passed in row, col in dem to return an xyz. Will return null if index is out of bounds, or point should be masked out.
        /// </summary>
        /// <param name="dem"></param>
        /// <param name="mask"></param>
        /// <param name="row"></param>
        /// <param name="col"></param>
        /// <param name="filterValues"></param>
        /// <returns></returns>
        public static Vector3? GetXYZ(Image dem, Mask mask, int row, int col, double scale = 1, bool filterValues = true, double minFilter = -1000000, double maxFilter = 1000000)
        {
            if (row < 0 || row >= dem.Height || col < 0 || col >= dem.Width || !dem.IsValid(row, col)) //respect input image mask if it has one
            {
                return null;
            }

            double value = dem[0, row, col];
            if (!filterValues || value >= minFilter && value <= maxFilter)
            {
                if (mask != null && !mask.isValid(row, col))
                {
                    return null;
                }
                return dem.CameraModel.Unproject(new Vector2(col, row), -1 * value * scale);
            }
            return null;
        }

        public static Vector3? GetXYZ(Image dem, int row, int col, double scale = 1, bool filterValues = true, double minFilter = -1000000, double maxFilter = 1000000)
        {
            return GetXYZ(dem, null, row, col, scale, filterValues, minFilter, maxFilter);
        }

        public static Vector2? GetRowCol(Image dem, Vector3 xyz)
        {
            return dem.CameraModel.Project(xyz, out double range);
        }

        /// <summary>
        /// Given Image dem, find corners that are not masked out. Optionally enter top left corner and a size parameter to get corners of a subregion.
        /// May not return a full set of vertices (potentially none) if image heavily masked
        /// </summary>
        /// <param name="dem"></param>
        /// <param name="minRow"></param>
        /// <param name="minCol"></param>
        /// <param name="width"></param>
        /// <param name="height"></param>
        /// <returns></returns>
        public static List<Vector2> FindCorners(Image dem, int minRow = 0, int minCol = 0, int width = -1, int height = -1)
        {
            int diagonalLength = 0;
            bool foundTopLeft = false;
            bool foundTopRight = false;
            bool foundBotLeft = false;
            bool foundBotRight = false;
            if (width == -1)
            {
                width = dem.Width - minCol - 1;
            }
            if (height == -1)
            {
                height = dem.Height - minRow - 1;
            }

            List<Vector2> ret = new List<Vector2>();
            while (diagonalLength < Math.Min(width, height) && (!foundTopLeft || !foundTopRight || !foundBotLeft || !foundBotRight))
            {
                int col;
                for (int row = 0; row <= diagonalLength; row++)
                {
                    col = diagonalLength - row;
                    if (!foundTopLeft)
                    {
                        Vector3? tl = DemOperations.GetXYZ(dem, null, minRow + row, minCol + col);
                        if (tl.HasValue)
                        {
                            foundTopLeft = true;
                            ret.Add(new Vector2(minCol + col, minRow + row));
                        }
                    }
                    if (!foundTopRight)
                    {
                        Vector3? tr = DemOperations.GetXYZ(dem, null, minRow + row, minCol + width - col);
                        if (tr.HasValue)
                        {
                            foundTopRight = true;
                            ret.Add(new Vector2(minCol + width - col, minRow + row));
                        }
                    }
                    if (!foundBotLeft)
                    {
                        Vector3? bl = DemOperations.GetXYZ(dem, null, minRow + height - row, minCol + col);
                        if (bl.HasValue)
                        {
                            foundBotLeft = true;
                            ret.Add(new Vector2(minCol + col, minRow + height - row));
                        }
                    }
                    if (!foundBotRight)
                    {
                        Vector3? br = DemOperations.GetXYZ(dem, null, minRow + height - row, minCol + width - col);
                        if (br.HasValue)
                        {
                            foundBotRight = true;
                            ret.Add(new Vector2(minCol + width - col, minRow + height - row));
                        }
                    }
                }
                ++diagonalLength;
            }
            return ret;
        }

        public static Matrix? AlignSceneToScene(Image scenemap, double sceneXOffsetMeters, double sceneYOffsetMeters, double sceneMetersPerPixel, Image otherScenemap, double otherSceneXOffsetMeters, double otherSceneYOffsetMeters, double otherSceneMetersPerPixel, bool preserveXY, int numAnnealingStages, SimulatedAnnealingOptions saOpts = null, double minOverlap = 0.5, int targetHeightmapRes = 256, double minFilter = -1000000, double maxFilter = 1000000)
        {
            return AlignScenesToScene(new Image[] { scenemap }, new double[] { sceneXOffsetMeters }, new double[] { sceneYOffsetMeters }, new double[] { sceneMetersPerPixel }, otherScenemap, otherSceneXOffsetMeters, otherSceneYOffsetMeters, otherSceneMetersPerPixel, preserveXY, numAnnealingStages, saOpts, minOverlap, targetHeightmapRes, minFilter, maxFilter);
        }

        public static Matrix? AlignScenesToScene(Image[] scenemaps, double[] sceneXOffsetsMeters, double[] sceneYOffsetsMeters, double[] sceneMetersPerPixel, Image otherScenemap, double otherSceneXOffsetMeters, double otherSceneYOffsetMeters, double otherSceneMetersPerPixel, bool preserveXY, int numAnnealingStages, SimulatedAnnealingOptions saOpts = null, double minOverlap = 0.5, int targetHeightmapRes = 256, double minFilter = -1000000, double maxFilter = 1000000)
        {
            double centerToOriginRows = otherSceneXOffsetMeters / otherSceneMetersPerPixel;
            double centerToOriginCols = -1 * otherSceneYOffsetMeters / otherSceneMetersPerPixel;
            double rowOrigin = otherScenemap.Height / 2.0 + centerToOriginRows;
            double colOrigin = otherScenemap.Width / 2.0 + centerToOriginCols;
            Matrix? otherDemToScene = AlignScenesToDem(scenemaps, sceneXOffsetsMeters, sceneYOffsetsMeters, sceneMetersPerPixel, otherScenemap, rowOrigin, colOrigin, otherSceneMetersPerPixel, preserveXY, numAnnealingStages, saOpts, minOverlap, targetHeightmapRes, minFilter, maxFilter);
            if (otherDemToScene == null)
            {
                return null;
            }
            else
            {
                Matrix offset = Matrix.CreateTranslation(-1 * otherSceneXOffsetMeters, -1 * otherSceneYOffsetMeters, 0);
                return offset * demToSitedriveCoordinateFlip * otherDemToScene;
            }
        }

        public static Matrix? AlignSceneToDem(Image scenemap, double sceneXOffsetMeters, double sceneYOffsetMeters, double sceneMetersPerPixel, Image dem, double demRowOffset, double demColOffset, double demMetersPerPixel, bool preserveXY, int numAnnealingStages, SimulatedAnnealingOptions saOpts = null, double minOverlap = 0.5, int targetHeightmapRes = 256, double minFilter = -1000000, double maxFilter = 1000000, Matrix[] priorTransforms = null)
        {
            return AlignScenesToDem(new[] { scenemap }, new[] { sceneXOffsetMeters }, new[] { sceneYOffsetMeters }, new[] { sceneMetersPerPixel }, dem, demRowOffset, demColOffset, demMetersPerPixel, preserveXY, numAnnealingStages, saOpts, minOverlap, targetHeightmapRes, minOverlap, maxFilter, priorTransforms);
        }

        /// <summary>
        /// Returns alignment from dem to first passed in scenemap (using samples from full list)
        /// </summary>
        /// <param name="scenemaps"></param>
        /// <param name="sceneXOffsetsMeters"></param>
        /// <param name="sceneYOffsetsMeters"></param>
        /// <param name="sceneMetersPerPixel"></param>
        /// <param name="dem"></param>
        /// <param name="newDemRowOffset"></param>
        /// <param name="newDemColOffset"></param>
        /// <param name="demMetersPerPixel"></param>
        /// <param name="preserveXY"></param>
        /// <param name="minOverlap"></param>
        /// <param name="targetHeightmapRes"></param>
        /// <param name="minFilter"></param>
        /// <param name="maxFilter"></param>
        /// <returns></returns>
        public static Matrix? AlignScenesToDem(Image[] scenemaps, double[] sceneXOffsetsMeters, double[] sceneYOffsetsMeters, double[] sceneMetersPerPixel, Image dem, double demRowOffset, double demColOffset, double demMetersPerPixel, bool preserveXY, int numAnnealingStages, SimulatedAnnealingOptions saOpts = null, double minOverlap = 0.5, int targetHeightmapRes = 256, double minFilter = -1000000, double maxFilter = 1000000, Matrix[] priorTransforms = null)
        {
            Random rand = NumberHelper.MakeRandomGenerator();

            int sceneCount = scenemaps.Count();

            if(sceneXOffsetsMeters.Count() != sceneCount ||
               sceneYOffsetsMeters.Count() != sceneCount ||
               sceneMetersPerPixel.Count() != sceneCount)
            {
                throw new Exception("Number of scenemaps does not match number of offsets.");
            }

            Matrix baseOffset = Matrix.CreateTranslation(new Vector3(sceneXOffsetsMeters[0], sceneYOffsetsMeters[0], 0)); //In site drive

            int succeeded = 0;
            int total = 0;

            List<Vector3> sceneSamples = new List<Vector3>();

            for (int i = 0; i < scenemaps.Count(); i++)
            {
                //demRowCenter, demColCenter corresponds to the origin of scene
                //Heightmap image center has an offset (not the origin)
                //Therefore we move the dem center to match the scene heightmap center
                //This translation is undone in the outputted transform

                //In dem space
                //  +X = North = -Row
                //  +Y = East  =  Col
                double newDemRowOffset = demRowOffset - sceneXOffsetsMeters[i] / demMetersPerPixel;
                double newDemColOffset = demColOffset + sceneYOffsetsMeters[i] / demMetersPerPixel;
                double sceneM2P = sceneMetersPerPixel[i];
                Matrix priorTransform = priorTransforms != null ? priorTransforms[i] : Matrix.Identity;
                Matrix currentOffset = Matrix.CreateTranslation(sceneXOffsetsMeters[i], sceneYOffsetsMeters[i], 0);
                priorTransform = demToSitedriveCoordinateFlip * currentOffset * priorTransform * Matrix.Invert(baseOffset) * demToSitedriveCoordinateFlip;

                Image scenemap = scenemaps[i];

                scenemap.CameraModel = new OrthographicCameraModel(Matrix.Identity, scenemap.Width, scenemap.Height, sceneM2P);

                for (int r = 0; r < scenemap.Height; r++)
                {
                    for (int c = 0; c < scenemap.Width; c++)
                    {
                        Vector3? scenePoint = GetXYZ(scenemap, r, c);
                        if (scenePoint.HasValue)
                        {
                            Vector3 transformedScenePoint = Vector3.Transform(scenePoint.Value, priorTransform);
                            Vector2? demRowCol = GetRowCol(dem, transformedScenePoint);
                            double rowOffsetMeters = (r - scenemap.Height / 2.0 + 0.5) * sceneM2P; //TODO + 0.5 pixel??
                            double colOffsetMeters = (c - scenemap.Width / 2.0 + 0.5) * sceneM2P;

                            //Ensure that samples are taken where meshes overlap in projected space
                            if (demRowCol.HasValue) {
                                //demRowOffset + rowOffsetMeters * demMetersPerPixel, demColOffset + colOffsetMeters * demMetersPerPixel
                                Vector3? demPoint = GetInterpolatedXYZ(dem, newDemRowOffset + rowOffsetMeters * demMetersPerPixel, newDemColOffset + colOffsetMeters * demMetersPerPixel, minFilter, maxFilter);
                                if (demPoint.HasValue)
                                {
                                    succeeded++;
                                    sceneSamples.Add(transformedScenePoint);
                                }
                            }
                            total++;
                        }
                    }
                }
            }

            if (succeeded / (double)total < minOverlap)
            {
                logger.InfoFormat("Overlap {0}/{1} is insufficient. Min ratio is {2}", succeeded, total, minOverlap);
                return null;
            }

            if (succeeded == 0)
            {
                throw new Exception("No overlap for heightmap align.");
            }

            double demHorizontalOrigin = dem.Width / 2.0;
            double demVerticalOrigin = dem.Height / 2.0;

            demRowOffset = demRowOffset - sceneXOffsetsMeters[0] / demMetersPerPixel;
            demColOffset = demColOffset + sceneYOffsetsMeters[0] / demMetersPerPixel;

            double[] guess = { 0, 0, 0, -1 * (demColOffset - demHorizontalOrigin) * demMetersPerPixel, -1 * (demVerticalOrigin - demRowOffset) * demMetersPerPixel }; //inverse sitedrive offset
            double[] sigma = new double[] { Math.PI / 2880, Math.PI / 2880, Math.PI / 2880, 0.02, 0.02 };

            if(preserveXY)
            {
                sigma[3] = 0;
                sigma[4] = 0;
            }

            double zTranslation = 0;

            Func<Quaternion, Vector3, double[]> transformToArray = new Func<Quaternion, Vector3, double[]>((r, t) =>
            {
                AxisAngleVector aav = new AxisAngleVector(r);
                return new double[]
                {
                    aav.X, aav.Y, aav.Z,
                    t.X, t.Y
                };
            });

            Func<double[], Matrix> arrayToTransform = new Func<double[], Matrix>((transform) =>
            {
                AxisAngleVector aav = new AxisAngleVector(transform[0], transform[1], transform[2]);
                Quaternion rotation = aav.ToQuaternion();
                Vector3 translation = new Vector3(transform[3], transform[4], zTranslation);
                return Matrix.CreateFromQuaternion(rotation) * Matrix.CreateTranslation(translation);
            });

            Func<double[], double> meanZSquaredError = new Func<double[], double>((transformArray) => {
                double error = 0;
                //Aligning scene sample points to dem; final transform will be dem to scene. This could be refactored to avoid invert but should not make much of a computational difference
                Matrix transformMatrix = Matrix.Invert(arrayToTransform(transformArray));
                int count = 0;
                for (int i = 0; i < sceneSamples.Count; i++)
                {
                    Vector3 sceneXYZ = Vector3.Transform(sceneSamples[i], transformMatrix);
                    //Project the transformed scene point onto dem
                    Vector2? demRowCol = GetRowCol(dem, sceneXYZ);
                    if (demRowCol.HasValue)
                    {
                        //Unproject to get dem height
                        Vector3? demXYZ = GetInterpolatedXYZ(dem, demRowCol.Value.Y, demRowCol.Value.X, minFilter, maxFilter);
                        //TODO: Issue 644 - better way to handle when the scene samples no longer hit the dem? Should be rare for orbital but could be a problem for more general use case
                        if (demXYZ.HasValue) {
                            double zOff = sceneXYZ.Z - demXYZ.Value.Z;
                            error += zOff * zOff;
                            ++count;
                        }
                    }
                }
                return count == 0 ? double.MaxValue : error / (double)count;
            });

            //Use a vertical projection to get height offset
            Func<double> meanZOffset = new Func<double>(() =>
            {
                double x = 0;
                int count = 0;
                for (int i = 0; i < sceneSamples.Count; i++)
                {
                    Vector3 sceneXYZ = Vector3.Transform(sceneSamples[i], Matrix.Invert(arrayToTransform(guess))); //See comment about invert in meanZError
                    Vector2? demRowCol = GetRowCol(dem, sceneXYZ);
                    if (demRowCol.HasValue)
                    {
                        Vector3? demXYZ = GetInterpolatedXYZ(dem, demRowCol.Value.Y, demRowCol.Value.X, minFilter, maxFilter); //TODO: check half pixel on interpolate
                        if (demXYZ.HasValue)
                        {
                            x += demXYZ.Value.Z - sceneXYZ.Z;
                            ++count;
                        }
                    }
                }
                return count == 0 ? 0 : x / (double)count;
            });

            zTranslation = -1 * meanZOffset();

            SimulatedAnnealing sa = new SimulatedAnnealing();
            if (saOpts == null)
            {
                saOpts = new SimulatedAnnealingOptions();
                saOpts.maxIterations = 400;
                saOpts.verbose = false;
                saOpts.temperatureScale = 1;
                saOpts.probabilityScale = 100;
                saOpts.sigma = sigma;
            }
            sa.opts = saOpts;

            for (int i = 0; i < numAnnealingStages; i++)
            {
                logger.InfoFormat("Annealing pass {0}/{1} :  Error = {2}", i + 1, numAnnealingStages, meanZSquaredError(guess));
                sa.temperatureExponent = 1.0 / (Math.Max(4, numAnnealingStages) - i);
                double[] saTransform = sa.Minimize(meanZSquaredError, guess);
                guess = saTransform;
                zTranslation -= meanZOffset();
            }

            logger.InfoFormat("Finished annealing. Final error = {0}", meanZSquaredError(guess));

            //Include dem mesh -> site drive coordinate flip in returned transform
            //Alignment operates on portion of dem corresponding to scene heightmap center, not scene origin.
            //Factor in scene heightmap offset into final dem transform.

            return arrayToTransform(guess) * demToSitedriveCoordinateFlip * baseOffset; //transfrom applied before flip (row vectors)
        }
    }
}
