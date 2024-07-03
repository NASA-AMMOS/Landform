using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using Microsoft.Xna.Framework;
using JPLOPS.Util;
using JPLOPS.MathExtensions;

namespace JPLOPS.Geometry
{
    /// <summary>
    /// Contains methods for placing Poisson-disc point samples across the surface of a given mesh at a chosen density
    /// Based upon the paper "Efficient and Flexible Sampling with BlueNoise Properties of Triangular Meshes"
    /// by Massimiliano Corsini, Paolo Cignoni, and Roberto Scopigno
    /// http://ieeexplore.ieee.org/document/6143943/
    /// </summary>
    public class SurfacePointSampler
    {
        private Random random;
        private int randomSeed;

        /// <summary>
        /// Constructs a SurfacePointSampler with an optional seed to specify the deterministic sample results
        /// </summary>
        /// <param name="seed">Seed for the random generator to provide deterministic samples</param>
        public SurfacePointSampler(int seed)
        {
            randomSeed = seed;
        }

        public SurfacePointSampler()
        {
            randomSeed = NumberHelper.RandomSeed ?? 0;
        }

        /// <summary>
        /// Generates and returns a point cloud mesh of samples across the surface of a given mesh at a specified
        /// density
        /// </summary>
        /// <param name="input">Mesh which will have points sampled across its surface</param>
        /// <param name="density">Factor that controls the density and quantity of points needed to cover the mesh
        /// surface area.  This is a metric that approximates how many points to place on a flat surface within a given
        /// square unit area, where denser = more points.</param>
        /// <param name="presampleFactor">Factor of how many points to sample randomly before pruning away ones that are
        /// too close together</param>
        /// <returns>New mesh containing a point cloud of samples across the surface of the given mesh</returns>
        public Mesh GenerateSampledMesh(Mesh input, double density, int presampleFactor = 20,
                                        bool normalizeNormals = true, double area = -1)
        {
            Vertex[] sampled = Sample(input, density, presampleFactor, normalizeNormals, false, area);
            Mesh pointCloud = new Mesh(hasNormals: input.HasNormals, hasColors: input.HasColors, hasUVs: input.HasUVs);
            pointCloud.Vertices = new List<Vertex>(sampled);
            return pointCloud;
        }

        public static double DensityToSampleSpacing(double density)
        {
            // Calculate the minimum allowed radius between points after pruning
            //
            // Four circles packed into a grid with their overlapping radiuses separating them form a square defined at
            // its edges by the circle centers. This area is about 1/4 (or 0.25) the circle areas.
            //
            // return 1 / Math.Sqrt(density) * 0.25;
            //
            // see here: https://mathoverflow.net/a/124740
            return 1 / Math.Sqrt(2 * density);
        }

        /// <summary>
        /// Generates and returns a point cloud vertex array of samples across the surface of a given mesh at a
        /// specified density
        /// </summary>
        /// <param name="input">Mesh which will have points sampled across its surface</param>
        /// <param name="density">Factor that controls the density and quantity of points needed to cover the surface
        /// mesh surface area.  This is a metric that approximates how many points to place on a flat surface within a
        /// given square unit area, where denser = more points.</param>
        /// <param name="presampleFactor">Factor of how many points to sample randomly before pruning away ones that
        /// are too close together</param>
        /// <returns>Vertex array containing a point cloud of samples across the surface of the given mesh</returns>
        public Vertex[] Sample(Mesh input, double density, int presampleFactor = 20, bool normalizeNormals = true,
                               bool positionsOnly = false, double area = -1)
        {
            random = new Random(randomSeed);

            if (area < 0)
            {
                area = input.SurfaceArea();
            }

            if (area < 1e-6)
            {
                throw new Exception("cannot sample zero area mesh");
            }

            // quantity of points to presample randomly across the surface which are later pruned
            int presampleQuantity = (int)(density * presampleFactor * area);

            double radius = DensityToSampleSpacing(density);

            // Calculate the size of each cell in which points are bucketed
            // This is sized such that a cell holding a point must be fully encompassed within its exclusion region
            // intuition: in the worst case a point may be at the corner of its cell
            // and we want the exclusion circle centered on it to touch the opposite corner of the cell
            double cellSize = radius / Math.Sqrt(2);

            Vector3WithTri[] oversampledPoints = PlacePointsOnSurface(input, presampleQuantity);

            var cells = FillCells(oversampledPoints, cellSize, presampleFactor);

            List<Vector3Int> shuffledCells = NumberHelper.Shuffle(cells.Keys.ToList(), random);

            Vector3WithTri[] prunedPoints = Prune(cells, shuffledCells, radius);

            return GenerateVertices(prunedPoints, input, normalizeNormals, positionsOnly);
        }

        /// <summary>
        /// Performs monte carlo sampling of points randomly and uniformly across the given mesh surface
        /// </summary>
        /// <param name="input">Mesh which will have points distributed randomly across its surface</param>
        /// <param name="quantity">Exact number of points to place on the surface</param>
        /// <returns>Array of Vector3/Triangle pairs holding the coordinates of the point and the triangle it lies
        /// on</returns>
        private Vector3WithTri[] PlacePointsOnSurface(Mesh input, int quantity)
        {
            double[] runningTriAreas = new double[input.Faces.Count];
            double surfaceArea = 0;
            for (int i = 0; i < runningTriAreas.Length; i++)
            {
                surfaceArea += input.FaceToTriangle(i).Area();
                runningTriAreas[i] = surfaceArea;
            }

            // Random instance is not threadsafe, so create one for each thread
            Vector3WithTri[] samples = new Vector3WithTri[quantity];
            CoreLimitedParallel.For(0, samples.Length,
                                    () => new Random(random.Next()),
                                    (i, rng) => {
                                        samples[i] = PickPointOnMesh(input, runningTriAreas, surfaceArea, rng);
                                        return rng;
                                    },
                                    rng => {});
            return samples;
        }

        /// <summary>
        /// Picks a random face with probability proportional to its area in the mesh and places a point randomly within
        /// its triangle
        /// </summary>
        /// <param name="input">the input mesh</param>
        /// <param name="runningTriAreas">Surface area of all triangles up to and including the nth triangle in the
        /// input mesh face list</param>
        /// <param name="surfaceArea">Total surface area of all triangles in the mesh</param>
        /// <returns>Randomly picked point on a random triangle represented as a coordinate Vector3/Triangle reference
        ///pair</returns>
        private Vector3WithTri PickPointOnMesh(Mesh input, double[] runningTriAreas, double surfaceArea, Random rng)
        {
            // Pick a face weighted by its area
            double chosenFaceRunningArea = rng.NextDouble() * surfaceArea;

            int first = 0;
            int last = runningTriAreas.Length - 1;
            int middle;

            // Perform a binary search for the random area to pick the exact triangle with the matching area summation
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

            // Pick the index of the triangle from the binary search
            int index = runningTriAreas[first] >= chosenFaceRunningArea ? first : last;

            Triangle chosenTri = input.FaceToTriangle(index);
            return new Vector3WithTri(chosenTri.RandomPoint(rng), chosenTri);
        }

        /// <summary>
        /// Places a set of Vector3/Triangle pairs into a dictionary of cells of a specified size
        /// </summary>
        /// <param name="oversampledPoints">Array of coordinate Vector3/Triangle reference pairs</param>
        /// <param name="cellSize">Length/width/height of each cell</param>
        /// <returns>
        /// Dictionary of the cells containing an unordered list of the points within corresponding cells,
        /// where each cell is one multiple of the cell size apart
        /// </returns>
        private IDictionary<Vector3Int, UnorderedList<Vector3WithTri>>
            FillCells(Vector3WithTri[] oversampledPoints, double cellSize, int presampleFactor)
        {
            int expectedPointsPerCell = presampleFactor;
            int expectedCells = oversampledPoints.Length / expectedPointsPerCell;
            var cells = new ConcurrentDictionary<Vector3Int, UnorderedList<Vector3WithTri>>
                (concurrencyLevel: CoreLimitedParallel.GetMaxCores(), capacity: expectedCells);

            CoreLimitedParallel.ForEach(oversampledPoints, point => {
                    
                    int x = (int)(point.coordinates.X / cellSize);
                    int y = (int)(point.coordinates.Y / cellSize);
                    int z = (int)(point.coordinates.Z / cellSize);
                    Vector3Int coord = new Vector3Int(x, y, z);
                    
                    var list = cells.GetOrAdd(coord, cc => new UnorderedList<Vector3WithTri>(expectedPointsPerCell));
                    lock (list)
                    {
                        list.Add(point);
                    }
                });

            return cells;
        }

        /// <summary>
        /// Remove all points within a given radius from remaining randomly selected points and return them as a new
        /// array of point/triangle pairs
        /// </summary>
        /// <param name="cells">Dictionary of cells linking to the points within a cell territory</param>
        /// <param name="shuffledCells">Shuffled list of cell coordinates used to dictate the order of removal</param>
        /// <param name="radius">Distance in which neighboring points should be removed from neighboring cells</param>
        /// <returns>Aray of point/triangle pairs where no point is within the given radius from one another</returns>
        private Vector3WithTri[] Prune(IDictionary<Vector3Int, UnorderedList<Vector3WithTri>> cells,
                                       List<Vector3Int> shuffledCells, double radius)
        {
            double radiusSquared = radius * radius;
            List<Vector3WithTri> remainingPoints = new List<Vector3WithTri>(cells.Count);
            //CoreLimitedParallel NONTRIVIAL
            Serial.ForEach(shuffledCells, () => new Random(random.Next()), (occupiedCell, rng) => {

                    UnorderedList<Vector3WithTri> pointsInCell = cells[occupiedCell];

                    Vector3WithTri randomPoint = null;
                    lock (pointsInCell)
                    {
                        // If this cell was already emptied by past runs of the loop, skip this cell
                        if (pointsInCell.Count == 0)
                        {
                            return rng;
                        }
                    
                        // Keep one random point from this cell
                        randomPoint = pointsInCell[(int)(pointsInCell.Count * rng.NextDouble())];

                        // Empty all the points from this cell
                        pointsInCell.Empty();
                    }
                    
                    lock (remainingPoints)
                    {
                        remainingPoints.Add(randomPoint);
                    }

                    // Remove points from other cells that are within the radius of this chosen point
                    // Points can be in cells as far as 2 away from the original point's cell
                    for (int x = -2; x <= 2; x++)
                    {
                        for (int y = -2; y <= 2; y++)
                        {
                            for (int z = -2; z <= 2; z++)
                            {
                                // Remove points from the cell with the current loop iteration's XYZ offset
                                ClearPointsNearPoint(radiusSquared, randomPoint.coordinates,
                                                     occupiedCell.offset(x, y, z), cells);
                            }
                        }
                    }

                    return rng;
                },
                rng => {});

            return remainingPoints.ToArray();
        }

        /// <summary>
        /// Removes all points from a cell specified by its coordinates within a given distance of a given point
        /// </summary>
        /// <param name="radiusSquared">Square of the radius within which points should be removed</param>
        /// <param name="point">Point in space around which all points should be removed from the cell</param>
        /// <param name="cellCoordinates">Coordinates of the cell in integer cell-size-multiple space</param>
        /// <param name="cells">Dictionary of all cells with their point lists</param>
        private void ClearPointsNearPoint(double radiusSquared, Vector3 point, Vector3Int cellCoordinates,
                                          IDictionary<Vector3Int, UnorderedList<Vector3WithTri>> cells)
        {
            // The dictionary may not contain the requested cell because it can be requested as an offset never
            // originally added as a cell with points
            if (cells.ContainsKey(cellCoordinates))
            {
                // Cell which is being queried for its points
                UnorderedList<Vector3WithTri> current = cells[cellCoordinates];
                lock (current)
                {
                    for (int i = 0; i < current.Count; )
                    {
                        // If within range, remove the point and don't increment i
                        if ((current[i].coordinates - point).LengthSquared() < radiusSquared)
                        {
                            current.Remove(i);
                        }
                        // If out of range, don't remove a point but increment i
                        else
                        {
                            i++;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Convert a point/triangle pair into a vertex with its normal, UV, and color properties
        /// </summary>
        private Vertex[] GenerateVertices(Vector3WithTri[] points, Mesh input, bool normalizeNormals,
                                          bool positionsOnly)
        {
            Vertex[] vertices = new Vertex[points.Length];

            CoreLimitedParallel.For(0, points.Length, i => {

                    Vector3 point = points[i].coordinates;
                    Vertex vertex = new Vertex(point);
                    vertices[i] = vertex;
                    
                    if (!positionsOnly)
                    {
                        BarycentricPoint trianglePoint = points[i].triangle.ClosestPoint(point);
                        if (input.HasNormals)
                        {
                            vertex.Normal = trianglePoint.Normal;
                            if (normalizeNormals && vertex.Normal.LengthSquared() > 1e-6)
                            {
                                vertex.Normal.Normalize();
                            }
                        }
                        if (input.HasUVs)
                        {
                            vertex.UV = trianglePoint.UV;
                        }
                        if (input.HasColors)
                        {
                            vertex.Color = trianglePoint.Color;
                        }
                    }
                });

            return vertices;
        }

        /// <summary>
        /// Pairing between a Vector3 coordinate and a Triangle reference
        /// </summary>
        private class Vector3WithTri
        {
            public Vector3 coordinates;
            public Triangle triangle;

            /// <summary>
            /// Constructs a pairing given a Vector3 and a Triangle
            /// </summary>
            /// <param name="coordinates">Point on a triangle</param>
            /// <param name="triangle">Triangle which the point is on</param>
            public Vector3WithTri(Vector3 coordinates, Triangle triangle)
            {
                this.coordinates = coordinates;
                this.triangle = triangle;
            }
        }
    }
}
