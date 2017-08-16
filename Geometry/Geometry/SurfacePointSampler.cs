using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace OPS.Geometry
{
    /// <summary>
    /// Contains methods for placing Poisson-disc point samples across the surface of a given mesh at a chosen density
    /// </summary>
    public class SurfacePointSampler
    {
        // Random value initialized to a given seed before placing points
        private Random random;
        // Seed on which to base the sampled results for a mesh
        private int samplingSeed;

        /// <summary>
        /// Constructs a SurfacePointSampler with an optional seed to specify the deterministic sample results
        /// </summary>
        /// <param name="seed">Seed for the random generator to provide deterministic samples</param>
        public SurfacePointSampler(int seed = 0)
        {
            samplingSeed = seed;
        }

        /// <summary>
        /// Generates and returns a point cloud mesh of samples across the surface of a given mesh at a specified density
        /// </summary>
        /// <param name="input">Mesh which will have points sampled across its surface</param>
        /// <param name="density">Factor that controls the density and quantity of points needed to cover the surface mesh surface area</param>
        /// <param name="presampleFactor">Factor of how many points to sample randomly before pruning away ones that are too close together</param>
        /// <returns>New mesh containing a point cloud of samples across the surface of the given mesh</returns>
        public Mesh GenerateSampledMesh(Mesh input, double density, int presampleFactor = 20)
        {
            // Perform Poisson-disc sampling on the given mesh with the specified density and presample factor
            Vertex[] sampled = Sample(input, density, presampleFactor);

            // Build a list of vertices out of the vertex array
            List<Vertex> sampledVertexList = new List<Vertex>(sampled);

            // Construct a mesh for the sampled points
            Mesh pointCloud = new Mesh(hasNormals: input.HasNormals, hasColors: input.HasColors, hasUVs: input.HasUVs);

            // Place the vertices into the mesh
            pointCloud.Vertices = sampledVertexList;

            // Return the constructed mesh
            return pointCloud;
        }

        /// <summary>
        /// Generates and returns a point cloud vertex array of samples across the surface of a given mesh at a specified density
        /// </summary>
        /// <param name="input">Mesh which will have points sampled across its surface</param>
        /// <param name="density">Factor that controls the density and quantity of points needed to cover the surface mesh surface area</param>
        /// <param name="presampleFactor">Factor of how many points to sample randomly before pruning away ones that are too close together</param>
        /// <returns>Vertex array containing a point cloud of samples across the surface of the given mesh</returns>
        public Vertex[] Sample(Mesh input, double density, int presampleFactor = 20)
        {
            // Make the random generator deterministic upon sampling each model independent of one another
            random = new Random(samplingSeed);

            // Calculate the quantity of points to presample randomly across the surface which are later pruned
            int presampleQuantity = (int)(density * presampleFactor * input.SurfaceArea());

            // Calculate the minimum allowed radius between points after pruning
            double radius = 1 / Math.Sqrt(density) * 0.25;

            // Calculate the size of each cell in which points are bucketed
            // This is sized such that a cell holding a point must be fully encompassed within its exclusion region
            double cellSize = radius / Math.Sqrt(2);

            // Pre-sample points, bucket them into a cell dictionary, shuffle dictionary keys, prune points in each cell, and convert them into vertices
            Vector3WithTri[] oversampledPoints = PlacePointsOnSurface(input, presampleQuantity);
            Dictionary<Vector3Int, UnorderedList<Vector3WithTri>> cells = FillCells(oversampledPoints, cellSize);
            List<Vector3Int> shuffledCells = Shuffle(cells.Keys.ToList());
            Vector3WithTri[] prunedPoints = Prune(cells, shuffledCells, radius);
            Vertex[] verticesWithNormals = GenerateVertices(prunedPoints, input);

            // Return an array of pruned vertices
            return verticesWithNormals;
        }

        /// <summary>
        /// Performs monte carlo sampling of points randomly and uniformly across the given mesh surface
        /// </summary>
        /// <param name="input">Mesh which will have points distributed randomly across its surface</param>
        /// <param name="quantity">Exact number of points to place on the surface</param>
        /// <returns>Array of Vector3/Triangle pairs holding the coordinates of the point and the triangle it lies on</returns>
        private Vector3WithTri[] PlacePointsOnSurface(Mesh input, int quantity)
        {
            // Triangles in the input mesh
            List<Triangle> tris = input.Triangles();

            // Calculate the total surface area and the total surface area of each triangle encountered so far
            double[] runningTriAreas = new double[tris.Count];
            double surfaceArea = 0;
            for (int i = 0; i < runningTriAreas.Length; i++)
            {
                surfaceArea += tris[i].Area();
                runningTriAreas[i] = surfaceArea;
            }

            // Pre-generate oversampled set of points
            Vector3WithTri[] samples = new Vector3WithTri[quantity];
            int[] seeds = new int[quantity];
            for (int i = 0; i < seeds.Length; i++)
            {
                seeds[i] = random.Next();
            }
            Parallel.For(0, samples.Length, i => {
                samples[i] = PickPointOnMesh(tris, runningTriAreas, surfaceArea, seeds[i]);
            });

            // Point cloud of oversampled point/triangle pairs
            return samples;
        }

        /// <summary>
        /// Picks a random face with probability proportional to its area in the mesh and places a point randomly within its triangle
        /// </summary>
        /// <param name="tris">Triangles in the mesh</param>
        /// <param name="runningTriAreas">Surface area of all triangles up to and including the nth triangle in the tris list</param>
        /// <param name="surfaceArea">Total surface area of all triangles in the mesh</param>
        /// <returns>Randomly picked point on a random triangle represented as a coordinate Vector3/Triangle reference pair</returns>
        private Vector3WithTri PickPointOnMesh(List<Triangle> tris, double[] runningTriAreas, double surfaceArea, int seed)
        {
            // Use the given seed to make a random number generator for this thread's operations
            Random randomGenerator = new Random(seed);

            // Pick a face weighted by its area by selecting an area out of the total which a triangle's summation contains
            double chosenFaceRunningArea = randomGenerator.NextDouble() * surfaceArea;

            // Set up variables for the binary search
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

            // Choose the triangle that contains the randomly picked area
            Triangle chosenTri = tris[index];

            // Pick a random point on the surface of the randomly chosen triangle
            Vector3 randomPointOnChosenTri = PickPointOnTriangle(chosenTri, randomGenerator);

            // Return a point/triangle pairing between the random point and its triangle
            return new Vector3WithTri(randomPointOnChosenTri, chosenTri);
        }

        /// <summary>
        /// Picks a uniformly random point on the given triangle in barycentric coordinates and returns its cartesian coordinates
        /// </summary>
        /// <param name="tri">Triangle on which to place a point</param>
        /// <returns>Point in cartesian space of the random point on the triangle's surface</returns>
        private Vector3 PickPointOnTriangle(Triangle tri, Random randomGenerator)
        {
            // Prepare triangle vertices
            Vector3 v1 = tri.V0.Position;
            Vector3 v2 = tri.V1.Position;
            Vector3 v3 = tri.V2.Position;

            // Pick random coordinates across a square that gets squished onto the triangle
            double a = randomGenerator.NextDouble();
            double b = randomGenerator.NextDouble();

            // If the random coordinates cross the boundary of the triangle into the extents of the imaginary square, flip back onto the triangle
            if (a + b >= 1)
            {
                a = 1 - a;
                b = 1 - b;
            }

            // Map the square coordinates onto the triangle
            return v1 + a * (v2 - v1) + b * (v3 - v1);
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
        private Dictionary<Vector3Int, UnorderedList<Vector3WithTri>> FillCells(Vector3WithTri[] oversampledPoints, double cellSize)
        {
            // Construct a dictionary to hold a pairing between cell locations and unordered lists of bucketed points
            var cells = new Dictionary<Vector3Int, UnorderedList<Vector3WithTri>>(oversampledPoints.Length);

            // Place all points into their corresponding cell
            foreach (Vector3WithTri point in oversampledPoints)
            {
                // Find the cell coordinates of the cell to hold this point
                int x = (int)(point.coordinates.X / cellSize);
                int y = (int)(point.coordinates.Y / cellSize);
                int z = (int)(point.coordinates.Z / cellSize);
                Vector3Int cellCoordinate = new Vector3Int(x, y, z);

                // Add an empty cell to the dictionary if it isn't in the dictionary yet
                if (!cells.ContainsKey(cellCoordinate))
                {
                    cells.Add(cellCoordinate, new UnorderedList<Vector3WithTri>());
                }

                // Place the point into the cell
                cells[cellCoordinate].Add(point);
            }

            // Return the cell dictionary
            return cells;
        }

        /// <summary>
        /// Shuffles a given list of Vector3Ints and returns itself for convenience
        /// </summary>
        /// <param name="list">List of Vector3Ints to be shuffled in place</param>
        /// <returns>The given list that has been shuffled</returns>
        private List<Vector3Int> Shuffle(List<Vector3Int> list)
        {
            // Fisher–Yates shuffle
            for (int i = list.Count - 1; i >= 1; i--)
            {
                int j = random.Next(0, i + 1);
                Vector3Int temp = list[i];
                list[i] = list[j];
                list[j] = temp;
            }

            // Return the shuffled list
            return list;
        }

        /// <summary>
        /// Remove all points within a given radius from remaining randomly selected points and return them as a new array of point/triangle pairs
        /// </summary>
        /// <param name="cells">Dictionary of cells linking to the points within a cell territory</param>
        /// <param name="shuffledCells">Shuffled list of cell coordinates used to dictate the order of neighbor removal</param>
        /// <param name="radius">Distance in which neighboring points should be removed from neighboring cells</param>
        /// <returns>Aray of point/triangle pairs where no point is within the given radius from one another</returns>
        private Vector3WithTri[] Prune(Dictionary<Vector3Int, UnorderedList<Vector3WithTri>> cells, List<Vector3Int> shuffledCells, double radius)
        {
            // Save the radius squared to avoid the square root in distance checking
            double radiusSquared = radius * radius;

            // List of points that get selected to return
            List<Vector3WithTri> remainingPoints = new List<Vector3WithTri>();

            // Run through every cell in the dictionary that has any points in it
            foreach (Vector3Int occupiedCell in shuffledCells)
            {
                // The points within the cell from the dictionary
                UnorderedList<Vector3WithTri> pointsInCell = cells[occupiedCell];

                // If this cell was already emptied by past runs of the loop, skip this cell
                if (pointsInCell.Count == 0)
                {
                    continue;
                }

                // Pick a random point that resides in this cell
                int randomIndex = (int)(pointsInCell.Count * random.NextDouble());
                Vector3WithTri randomPoint = pointsInCell[randomIndex];

                // Add this chosen point to the results array
                remainingPoints.Add(randomPoint);

                // Empty all the points from this cell
                pointsInCell.Empty();

                // Remove points from other cells that are within the radius of this chosen point
                // Points can be in cells as far as 2 away from the original point's cell
                for (int x = -2; x <= 2; x++)
                {
                    for (int y = -2; y <= 2; y++)
                    {
                        for (int z = -2; z <= 2; z++)
                        {
                            // Remove points from the cell with the current loop iteration's XYZ offset
                            ClearPointsNearPoint(radiusSquared, randomPoint.coordinates, occupiedCell.offset(x, y, z), cells);
                        }
                    }
                }
            }

            // Return the resulting picked points from their list as an array
            return remainingPoints.ToArray();
        }

        /// <summary>
        /// Removes all points from a cell specified by its coordinates within a given distance of a given point
        /// </summary>
        /// <param name="radiusSquared">Square of the radius within which points should be removed</param>
        /// <param name="point">Point in space around which all points should be removed from the cell</param>
        /// <param name="cellCoordinates">Coordinates of the cell in integer cell-size-multiple space</param>
        /// <param name="cells">Dictionary of all cells with their point lists</param>
        private void ClearPointsNearPoint(double radiusSquared, Vector3 point, Vector3Int cellCoordinates, Dictionary<Vector3Int, UnorderedList<Vector3WithTri>> cells)
        {
            // The dictionary may not contain the requested cell because it can be requested as an offset never originally added as a cell with points
            if (cells.ContainsKey(cellCoordinates))
            {
                // Cell which is being queried for its points
                UnorderedList<Vector3WithTri> current = cells[cellCoordinates];

                // Loop from i = 0 -> Count - 1 (inclusive)
                int i = 0;
                while (i < current.Count)
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

        /// <summary>
        /// Convert a point/triangle pair into a vertex with its normal, UV, and color properties
        /// </summary>
        /// <param name="points"></param>
        /// <param name="input"></param>
        /// <returns></returns>
        private Vertex[] GenerateVertices(Vector3WithTri[] points, Mesh input)
        {
            // Array of vertices that will be returned when populated
            Vertex[] vertices = new Vertex[points.Length];

            // Convert each point/triangle pair into a fully fledged vertex
            for (int i = 0; i < points.Length; i++)
            {
                // Break the point and triangle into their own structures
                Vector3 point = points[i].coordinates;
                Triangle tri = points[i].triangle;

                // Generate a vertex at the point's coordinates
                Vertex vertex = new Vertex(point);

                // Pick a 2D point on the surface of the containing triangle
                BarycentricPoint trianglePoint = tri.ClosestPoint(point);

                // Add normals, UVs, and color if the source mesh has such properties
                if (input.HasNormals)
                {
                    vertex.Normal = trianglePoint.Normal;
                }
                if (input.HasUVs)
                {
                    vertex.UV = trianglePoint.UV;
                }
                if (input.HasColors)
                {
                    vertex.Color = trianglePoint.Color;
                }

                // Add this vertex to the final vertex array
                vertices[i] = vertex;
            }

            // Return the full array of vertices
            return vertices;
        }

        /// <summary>
        /// List that has no order but allows for constant-time removal and random access
        /// </summary>
        private class UnorderedList<T>
        {
            // Ordered list which the data is stored in
            private List<T> List = new List<T>();

            // Number of elements currently held in the unordered list
            public int Count { get; private set; }

            /// <summary>
            /// Adds an element to the list, which increases the element count
            /// </summary>
            /// <param name="item">Element to add to the unordered list</param>
            public void Add(T item)
            {
                List.Add(item);
                Count++;
            }

            /// <summary>
            /// Removes an element from the list at a given index by placing the last element in its place and decreasing the count
            /// </summary>
            /// <param name="index">Index of the element to be removed</param>
            public void Remove(int index)
            {
                Count--;
                List[index] = List[Count];
            }

            /// <summary>
            /// Empties the list by setting its count to 0
            /// </summary>
            public void Empty()
            {
                Count = 0;
            }

            /// <summary>
            /// Enables reading and writing of the element at a given index
            /// </summary>
            /// <param name="index">Index of the unordered list to read or write at</param>
            /// <returns>The value if being read, nothing if being written</returns>
            public T this[int index]
            {
                get
                {
                    return List[index];
                }
                set
                {
                    List[index] = value;
                }
            }
        }

        /// <summary>
        /// Acts as a Vector3 but with integer instead of double values
        /// </summary>
        private struct Vector3Int
        {
            public int X;
            public int Y;
            public int Z;

            /// <summary>
            /// Constructs from a regular Vector3 by truncating decimal places
            /// </summary>
            /// <param name="vector3">Vector3 value which gets its components cast to an integer</param>
            public Vector3Int(Vector3 vector3)
            {
                X = (int)vector3.X;
                Y = (int)vector3.Y;
                Z = (int)vector3.Z;
            }

            /// <summary>
            /// Constructs from X, Y, and Z coordinates
            /// </summary>
            /// <param name="X">X component</param>
            /// <param name="Y">Y component</param>
            /// <param name="Z">Z component</param>
            public Vector3Int(int X, int Y, int Z)
            {
                this.X = X;
                this.Y = Y;
                this.Z = Z;
            }

            /// <summary>
            /// Returns a new Vector3Int of this offset by the given coordinates
            /// </summary>
            /// <param name="x">X component offset</param>
            /// <param name="y">Y component offset</param>
            /// <param name="z">Z component offset</param>
            /// <returns>New Vector3Int that is the sum of this and the given coordinates</returns>
            public Vector3Int offset(int x, int y, int z)
            {
                return new Vector3Int(X + x, Y + y, Z + z);
            }

            /// <summary>
            /// Generates a hash code from the coordinate components
            /// </summary>
            /// <returns>The hash code of this Vector3Int</returns>
            public override int GetHashCode()
            {
                return (((X + 17) * 10037 + Y) * 137 + Z) * 37;
            }

            /// <summary>
            /// Returns the equality of this Vector3Int to another
            /// </summary>
            /// <param name="obj">Object to compare this against</param>
            /// <returns>True if this has the same coordinates, false if they are different</returns>
            public override bool Equals(object obj)
            {
                Vector3Int other = (Vector3Int)obj;
                return X == other.X && Y == other.Y && Z == other.Z;
            }
        }

        /// <summary>
        /// Pairing between a Vector3 coordinate and a Triangle reference
        /// </summary>
        private struct Vector3WithTri
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
