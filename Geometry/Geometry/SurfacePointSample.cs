using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Geometry.Geometry;

namespace OPS.Geometry
{
    public class SurfacePointSample
    {
        private static Random random = new Random();
        private static double PRESAMPLE_FACTOR = 20;

        public static Vertex[] Sample(Mesh input, double density)
        {
            int presampleQuantity = (int)(density * PRESAMPLE_FACTOR * input.SurfaceArea());
            double radius = 1 / Math.Sqrt(density) * 0.25;
            double cellSize = radius / Math.Sqrt(2);

            Vector3WithTri[] oversampledPoints = PlacePointsOnSurface(input, presampleQuantity);
            Dictionary<Vector3Int, List<Vector3WithTri>> cells = FillCells(oversampledPoints, cellSize);
            List<Vector3Int> shuffledCells = cells.Keys.OrderBy(key => random.Next()).ToList();
            Vector3WithTri[] prunedPoints = Prune(cells, shuffledCells, radius);
            Vertex[] verticesWithNormals = CalculateNormals(prunedPoints);

            return verticesWithNormals;
        }

        private static Vector3WithTri[] PlacePointsOnSurface(Mesh input, int quantity)
        {
            List<Triangle> tris = input.Triangles();

            Vector3[] triangleNormals = new Vector3[tris.Count];
            for (int i = 0; i < tris.Count; i++)
            {
                triangleNormals[i] = tris[i].Normal;
            }

            double[] runningTriAreas = new double[tris.Count];
            double surfaceArea = 0;
            for (int i = 0; i < runningTriAreas.Length; i++)
            {
                surfaceArea += tris[i].Area();
                runningTriAreas[i] = surfaceArea;
            }

            Vector3WithTri[] samples = new Vector3WithTri[quantity];
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] = PickPointOnMesh(tris, surfaceArea, runningTriAreas, triangleNormals);
            }

            return samples;
        }

        private static Vector3WithTri PickPointOnMesh(List<Triangle> tris, double surfaceArea, double[] runningTriAreas, Vector3[] triangleNormals)
        {
            double chosenFaceRunningArea = random.NextDouble() * surfaceArea;

            int first = 0;
            int last = runningTriAreas.Length - 1;
            int middle;

            while ((last - first) / 2 > 0)
            {
                middle = first + (last - first) / 2;

                if (runningTriAreas[middle] < chosenFaceRunningArea)
                {
                    first = middle + 1;
                }
                else
                {
                    last = middle;
                }
            }

            int index = runningTriAreas[first] >= chosenFaceRunningArea ? first : last;

            Triangle chosenTri = tris[index];
            Vector3 chosenTriNormal = triangleNormals[index];

            Vector3WithTri result = new Vector3WithTri(PickPointOnTriangle(chosenTri), chosenTri);

            return result;
        }

        private static Vector3 PickPointOnTriangle(Triangle tri)
        {
            Vector3 v1 = tri.V0.Position;
            Vector3 v2 = tri.V1.Position;
            Vector3 v3 = tri.V2.Position;

            double a = random.NextDouble();
            double b = random.NextDouble();
            if (a + b >= 1)
            {
                a = 1 - a;
                b = 1 - b;
            }

            return v1 + a * (v2 - v1) + b * (v3 - v1);
        }

        private static Dictionary<Vector3Int, List<Vector3WithTri>> FillCells(Vector3WithTri[] oversampledPoints, double cellSize)
        {
            var cells = new Dictionary<Vector3Int, List<Vector3WithTri>>(oversampledPoints.Length);

            foreach (Vector3WithTri point in oversampledPoints)
            {
                int x = (int)(point.coordinates.X / cellSize);
                int y = (int)(point.coordinates.Y / cellSize);
                int z = (int)(point.coordinates.Z / cellSize);

                Vector3Int cellCoordinate = new Vector3Int(x, y, z);

                if (!cells.ContainsKey(cellCoordinate)) cells.Add(cellCoordinate, new List<Vector3WithTri>());

                cells[cellCoordinate].Add(point);
            }

            return cells;
        }

        private static Vector3WithTri[] Prune(Dictionary<Vector3Int, List<Vector3WithTri>> cells, List<Vector3Int> occupiedCells, double radius)
        {
            double radiusSquared = radius * radius;

            List<Vector3WithTri> remainingPoints = new List<Vector3WithTri>();

            foreach (Vector3Int occupiedCell in occupiedCells)
            {
                List<Vector3WithTri> pointsInCell = cells[occupiedCell];

                if (pointsInCell.Count == 0) continue;

                int randomIndex = (int)(pointsInCell.Count * random.NextDouble());
                Vector3WithTri randomPoint = pointsInCell[randomIndex];

                remainingPoints.Add(randomPoint);

                for (int x = -2; x <= 2; x++)
                {
                    for (int y = -2; y <= 2; y++)
                    {
                        for (int z = -2; z <= 2; z++)
                        {
                            ClearPointsNearPoint(radiusSquared, randomPoint.coordinates, occupiedCell.offset(x, y, z), cells);
                        }
                    }
                }
            }

            return remainingPoints.ToArray();
        }

        private static void ClearPointsNearPoint(double radiusSquared, Vector3 point, Vector3Int cellCoordinates, Dictionary<Vector3Int, List<Vector3WithTri>> cells)
        {
            if (cells.ContainsKey(cellCoordinates))
            {
                cells[cellCoordinates] = cells[cellCoordinates].Where(current => (current.coordinates - point).LengthSquared() >= radiusSquared).ToList();
            }
        }

        private static Vertex[] CalculateNormals(Vector3WithTri[] points)
        {
            Vertex[] vertex = new Vertex[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                vertex[i] = new Vertex(points[i].coordinates, points[i].triangle.Normal);
            }
            return vertex;
        }
    }
}
