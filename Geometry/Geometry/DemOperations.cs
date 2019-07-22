using log4net;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Util;
using OPS.Imaging;

namespace OPS.Geometry
{
    public static class DemOperations
    {
        static ILog logger = LogManager.GetLogger(typeof(DemOperations));

        /// <summary>
        /// Do bilinear interpolation with potentially null points.
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <param name="tl"></param>
        /// <param name="tr"></param>
        /// <param name="bl"></param>
        /// <param name="br"></param>
        /// <returns></returns>
        public static Vector3? Bilinear(double x, double y, Vector3? tl, Vector3? tr, Vector3? bl, Vector3? br)
        {
            Vector3 ret = new Vector3(0, 0, 0);
            double area = 0;
            if (tl.HasValue)
            {
                ret += tl.Value * x * y;
                area += x * y;
            }
            if (tr.HasValue)
            {
                ret += tr.Value * (1 - x) * y;
                area += (1 - x) * y;
            }
            if (bl.HasValue)
            {
                ret += bl.Value * x * (1 - y);
                area += x * (1 - y);
            }
            if (br.HasValue)
            {
                ret += br.Value * (1 - x) * (1 - y);
                area += (1 - x) * (1 - y);
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
        /// <param name="xyz"></param>
        /// <param name="mask"></param>
        /// <param name="minR"></param>
        /// <param name="minC"></param>
        /// <param name="size"></param>
        /// <returns></returns>
        public static IEnumerable<Vector2> FindCorners(Image dem, int minR = 0, int minC = 0, int width = -1, int height = -1)
        {
            int d = 0;
            bool foundTopLeft = false;
            bool foundTopRight = false;
            bool foundBotLeft = false;
            bool foundBotRight = false;
            if (width == -1)
            {
                width = dem.Width - minC - 1;
            }
            if (height == -1)
            {
                height = dem.Height - minR - 1;
            }

            while (d < Math.Min(width, height) && (!foundTopLeft || !foundTopRight || !foundBotLeft || !foundBotRight))
            {
                int c;
                for (int r = 0; r <= d; r++)
                {
                    c = d - r;
                    if (!foundTopLeft)
                    {
                        Vector3? tl = DemOperations.GetXYZ(dem, null, minR + r, minC + c);
                        if (tl.HasValue)
                        {
                            foundTopLeft = true;
                            yield return new Vector2(minC + c, minR + r);
                        }
                    }
                    if (!foundTopRight)
                    {
                        Vector3? tr = DemOperations.GetXYZ(dem, null, minR + r, minC + width - c);
                        if (tr.HasValue)
                        {
                            foundTopRight = true;
                            yield return new Vector2(minC + width - c, minR + r);
                        }
                    }
                    if (!foundBotLeft)
                    {
                        Vector3? bl = DemOperations.GetXYZ(dem, null, minR + height - r, minC + c);
                        if (bl.HasValue)
                        {
                            foundBotLeft = true;
                            yield return new Vector2(minC + c, minR + height - r);
                        }
                    }
                    if (!foundBotRight)
                    {
                        Vector3? br = DemOperations.GetXYZ(dem, null, minR + height - r, minC + width - c);
                        if (br.HasValue)
                        {
                            foundBotRight = true;
                            yield return new Vector2(minC + width - c, minR + height - r);
                        }
                    }
                }
                ++d;
            }
        }

        /// <summary>
        /// Unprojects passed in row, col in dem to return an xyz. Will return null if index is out of bounds, or point should be masked out.
        /// </summary>
        /// <param name="dem"></param>
        /// <param name="mask"></param>
        /// <param name="row"></param>
        /// <param name="col"></param>
        /// <param name="parser"></param>
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
                if (mask != null)
                {
                    if (!mask.isValid(row, col))
                    {
                        return null;
                    }
                    mask.setInvalid(row, col); //Prevent this point from being sampled again
                }
                return dem.CameraModel.Unproject(new Vector2(col, row), -1 * value);
            }         
            return null;
        }

        public static Vector3? GetXYZ(Image dem, int row, int col, bool filterValues = true, double minFilter = -1000000, double maxFilter = 1000000)
        {
            return GetXYZ(dem, null, row, col, filterValues, minFilter, maxFilter);
        }

        public static Matrix Align(Mesh scene, Image dem, double rowCenter, double colCenter, int width, int height, double metersPerPixel, double zOffsetGuess = 0, string sceneHeightmapPath = "")
        {
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
            List<Vector3> demSamples = new List<Vector3>();

            for (int r = 0; r < scenemap.Height; r++)
            {
                for (int c = 0; c < scenemap.Width; c++)
                {
                    Vector3? scenePoint = GetXYZ(scenemap, r, c);
                    if (scenePoint.HasValue)
                    {
                        Vector3? demPoint = GetXYZ(dem, demRowCenterInt - rowRadiusPixels + r, demColCenterInt - colRadiusPixels + c);
                        if (demPoint.HasValue)
                        {
                            sceneSamples.Add(scenePoint.Value);
                            demSamples.Add(demPoint.Value);
                        }
                    }
                }
            }

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

            Func<double[], double> sum_squared_error = new Func<double[], double>((transformArray) => {

                double error = 0;
                Matrix transformMatrix = arrayToTransform(transformArray);
                for (int i = 0; i < sceneSamples.Count; i++)
                {
                    Vector3 p1 = sceneSamples[i];
                    Vector3 p2 = Vector3.Transform(demSamples[i], transformMatrix);
                    error += Vector3.DistanceSquared(p1, p2);
                }
                return error;
            });

            Func<double> meanZOffset = new Func<double>(() =>
            {
                double x = 0;
                for (int i = 0; i < demSamples.Count; i++)
                {
                    x += sceneSamples[i].Z - Vector3.Transform(demSamples[i], arrayToTransform(guess)).Z;
                }
                return x / (double)demSamples.Count;
            });

            SimulatedAnnealing sa = new SimulatedAnnealing();
            sa.maxIterations = 10000;
            sa.noisy = true;
            sa.temperatureScale = 1;
            sa.probabilityScale = 100;
            int numAnnealStages = 10;
            for (int i = 0; i < numAnnealStages; i++)
            {
                logger.InfoFormat("Annealing pass {0}", i + 1);
                sa.temperatureExponent = 1.0 / (Math.Max(4, numAnnealStages) - i);
                double[] newTrans = sa.Minimize(sum_squared_error, guess, sigma);
                guess = newTrans;
                zTranslation += meanZOffset();
            }

            return arrayToTransform(guess);
        }
    }
}
