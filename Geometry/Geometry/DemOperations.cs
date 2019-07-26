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

        /// <summary>
        /// Unprojects passed in row, col in dem to return an xyz. Will return null if index is out of bounds, or point should be masked out.
        /// </summary>
        /// <param name="dem"></param>
        /// <param name="mask"></param>
        /// <param name="row"></param>
        /// <param name="col"></param>
        /// <param name="filterValues"></param>
        /// <returns></returns>
        public static Vector3? GetXYZ(Image dem, Mask mask, int row, int col, bool filterValues = true, double minFilter=-1000000, double maxFilter=1000000)
        {
            if (row < 0 || row >= dem.Height || col < 0 || col >= dem.Width || dem.IsInvalid(row, col)) //respect input image mask if it has one
            {
                return null;
            }

            var value = dem[0, row, col];
            if (!filterValues || value >= minFilter && value <= maxFilter)
            {
                if (mask != null && !mask.isValid(row, col))
                {
                   return null;
                }
                return dem.CameraModel.Unproject(new Vector2(col, row), -1 * value);
            }         
            return null;
        }

        public static Vector3? GetXYZ(Image dem, int row, int col, bool filterValues = true, double minFilter = -1000000, double maxFilter = 1000000)
        {
            return GetXYZ(dem, null, row, col, filterValues, minFilter, maxFilter);
        }

        public static Vector2? GetRowCol(Image dem, Vector3 xyz)
        {
            return dem.CameraModel.Project(xyz, out double range);
        }

        /// <summary>
        /// Align dem to given scene (centered approximately at rowCenter, colCenter in the dem). Algorithm only uses distance from sample dem points in a width x height box around given center to the mesh as fitness function.
        /// </summary>
        /// <param name="scene"></param>
        /// <param name="dem"></param>
        /// <param name="rowCenter"></param>
        /// <param name="colCenter"></param>
        /// <param name="width"></param>
        /// <param name="height"></param>
        /// <param name="metersPerPixel"></param>
        /// <param name="alignSamples"></param>
        /// <param name="zOffsetGuess"></param>
        /// <param name="sceneHeightmapPath"></param>
        /// <returns></returns>
        public static Matrix Align(Mesh scene, Image dem, double rowCenter, double colCenter, int width, int height, double metersPerPixel, out List<Vector3> alignSamples, double zOffsetGuess = 0, double percentToKeep=1.0, string sceneHeightmapPath = "")
        {
            Random rand = NumberHelper.MakeRandomGenerator();

            if (dem.CameraModel == null)
            {
                dem.CameraModel = new OrthographicCameraModel(Matrix.Identity, dem.Width, dem.Height, metersPerPixel);
            }

            double demRowCenterDouble = rowCenter;
            double demColCenterDouble = colCenter;
            int demRowCenterInt = (int)demRowCenterDouble;
            int demColCenterInt = (int)demColCenterDouble;

            //Correspond to Int centers above
            //Scene +X = North = -Row
            //Scene +Y = East = Col
            //Signs flip becuase casting subtracts difference above
            double sceneXCenter = demRowCenterDouble - demRowCenterInt - 0.5; //Correct fractional pixel offset to dem origin, and half pixel offset from projection
            double sceneYCenter = -1 * (demColCenterDouble - demColCenterInt) + 0.5;

            //Assume width/height are even and round down otherwise. This allows assuming even number of pixels to either side of origin.
            int rowRadiusPixels = height / 2;
            int xRadiusPixels = rowRadiusPixels;
            int colRadiusPixels = width / 2;
            int yRadiusPixels = colRadiusPixels;

            double xRadiusMeters = xRadiusPixels * metersPerPixel;
            double rowRadiusMeters = xRadiusMeters;
            double yRadiusMeters = yRadiusPixels * metersPerPixel;
            double colRadiusMeters = yRadiusMeters;

            BoundingBox sceneBounds = new BoundingBox(new Vector3(sceneXCenter - xRadiusMeters, sceneYCenter - yRadiusMeters, 0), new Vector3(sceneXCenter + xRadiusMeters, sceneYCenter + yRadiusMeters, 0));

            Image scenemap = MeshToHeightMap.BuildHeightMap(scene, sceneBounds, 2 * xRadiusPixels, 2 * yRadiusPixels).Item1;
            if (sceneHeightmapPath != "")
            {
                scenemap.Save<float>(sceneHeightmapPath);
            }
            scenemap.CameraModel = new OrthographicCameraModel(Matrix.Identity, scenemap.Width, scenemap.Height, metersPerPixel);

            List<Vector3> sceneSamples = new List<Vector3>();

            for (int r = 0; r < scenemap.Height; r++)
            {
                for (int c = 0; c < scenemap.Width; c++)
                {
                    Vector3? scenePoint = GetXYZ(scenemap, r, c);
                    if (scenePoint.HasValue)
                    {
                        Vector3? demPoint = GetXYZ(dem, demRowCenterInt - rowRadiusPixels + r, demColCenterInt - colRadiusPixels + c);
                        //Ensure that samples are taken where meshes overlap in projected space
                        if (demPoint.HasValue && rand.NextDouble() < percentToKeep)
                        {
                            sceneSamples.Add(scenePoint.Value);
                        }
                    }
                }
            }

            if(sceneSamples.Count < 1)
            {
                throw new Exception("Alignment found no samples. Check for sufficient overlap or consider increasing sample percentage.");
            }

            alignSamples = sceneSamples;

            double demHorizontalOrigin = dem.Width / 2.0;
            double demVerticalOrigin = dem.Height / 2.0;

            double[] guess = { 0, 0, 0, -1 * (demColCenterInt - demHorizontalOrigin) * metersPerPixel, -1 * (demVerticalOrigin - demRowCenterInt) * metersPerPixel }; //inverse sitedrive offset
            double[] sigma = new double[] { Math.PI / 2880, Math.PI / 2880, Math.PI / 2880, 0.02, 0.02 };
            double zTranslation = -1 * zOffsetGuess;

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

            //TODO: Could instead use a 2d projection with uv face tree, possibly more efficient (2d search instead of 3d), but would only use vertical projection rather than minimum distance
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
                        Vector3? demXYZ = Interpolate(demRowCol.Value.X - (int)demRowCol.Value.X, demRowCol.Value.Y - (int)demRowCol.Value.Y, 
                                GetXYZ(dem, (int)demRowCol.Value.Y, (int)demRowCol.Value.X),
                                GetXYZ(dem, (int)demRowCol.Value.Y, (int)Math.Ceiling(demRowCol.Value.X)),
                                GetXYZ(dem, (int)Math.Ceiling(demRowCol.Value.Y), (int)demRowCol.Value.X),
                                GetXYZ(dem, (int)Math.Ceiling(demRowCol.Value.Y), (int)Math.Ceiling(demRowCol.Value.X))
                            );
                        //TODO: better way to handle when the scene samples no longer hit the dem? Should be rare for orbital but could be a problem for more general use case
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
                        Vector3? demXYZ = GetXYZ(dem, (int)demRowCol.Value.Y, (int)demRowCol.Value.X);
                        if (demXYZ.HasValue)
                        {
                            x += demXYZ.Value.Z - sceneXYZ.Z;
                            ++count;
                        }
                    }
                }
                return count == 0 ? 0 : x / (double)count;
            });

            SimulatedAnnealing sa = new SimulatedAnnealing();
            sa.maxIterations = 100;
            sa.verbose = true;
            sa.temperatureScale = 1;
            sa.probabilityScale = 100;
            int numAnnealStages = 100;
            for (int i = 0; i < numAnnealStages; i++)
            {
                logger.InfoFormat("Annealing pass {0}", i + 1);
                sa.temperatureExponent = 1.0 / (Math.Max(4, numAnnealStages) - i);
                double[] newTrans = sa.Minimize(meanZSquaredError, guess, sigma);
                guess = newTrans;
                zTranslation += meanZOffset();
            }

            return arrayToTransform(guess);
        }
    }
}
