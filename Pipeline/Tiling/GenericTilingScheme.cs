using Microsoft.Xna.Framework;
using OPS.Geometry;
using OPS.Pipeline.TileServer;
using OPS.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Pipeline.Tiling
{
    public class TileNode
    {
        public TileNode(TileNode parent, BoundingBox bounds)
        {
            this.Parent = parent;
            this.Bounds = bounds;
            this.Children = new List<TileNode>();
            if (parent == null)
            {
                this.HasPosXNeighbor = false;
                this.HasNegXNeighbor = false;
                this.HasPosYNeighbor = false;
                this.HasNegYNeighbor = false;
                this.HasPosZNeighbor = false;
                this.HasNegZNeighbor = false;
            } else
            {
                this.HasPosXNeighbor = parent.HasPosXNeighbor;
                this.HasNegXNeighbor = parent.HasNegXNeighbor;
                this.HasPosYNeighbor = parent.HasPosYNeighbor;
                this.HasNegYNeighbor = parent.HasNegYNeighbor;
                this.HasPosZNeighbor = parent.HasPosZNeighbor;
                this.HasNegZNeighbor = parent.HasNegZNeighbor;
            }
        }

        public void ApplyRecursive(Action<TileNode> applyFunc)
        {
            applyFunc(this);
            foreach(TileNode child in this.Children)
            {
                child.ApplyRecursive(applyFunc);
            }
        }

        public IEnumerable<TileNode> GetLeaves()
        {
            if(this.Children.Count() == 0)
            {
                yield return this;
            } else
            {
                foreach(TileNode child in Children)
                {
                    foreach (TileNode res in child.GetLeaves())
                    {
                        yield return res;
                    }
                }
            }
        }

        public string id;
        public TileNode Parent;
        public IEnumerable<TileNode> Children;
        public BoundingBox Bounds;
        public bool HasPosXNeighbor;
        public bool HasNegXNeighbor;
        public bool HasPosYNeighbor;
        public bool HasNegYNeighbor;
        public bool HasPosZNeighbor;
        public bool HasNegZNeighbor;
    }

    public enum SplitDim
    {
        X, Y, Z
    }

    public enum SplitType
    {
        Weighted,
        Unweighted
    }

    public enum TreeType
    {
        N_ary, //quad, oct...
        KD //binary (with alternating split dimension)
    }

    public struct GenericTilingSchemeOptions
    {
        public ITileSplitCriteria tileSplitCriteria;
        /*public ITileSplitCriteria upperInfluenceRegion;
        public ITileSplitCriteria lowerInfluenceRegion;*/
        public SplitDim[] splitDims;
        public SplitType splitType;
        public TreeType treeType;
    }

    public class TriangleDensityCriteria : ITileSplitCriteria
    {
        int VertexPerTile;

        public TriangleDensityCriteria(int vertexPerTile)
        {
            this.VertexPerTile = vertexPerTile;
        }

        bool ITileSplitCriteria.ShouldSplit(MeshOperator meshOperator, BoundingBox bounds)
        {
            return meshOperator.CountVertices(bounds) > this.VertexPerTile;
        }
    }

    public class GenericTilingScheme
    {
        GenericTilingSchemeOptions Options;

        public GenericTilingScheme(GenericTilingSchemeOptions options)
        {
            this.Options = options;

            if (options.splitDims == null || options.splitDims.Length == 0)
            {
                throw new ArgumentException("Tiling requires split dimensions!");
            }
            if (options.tileSplitCriteria == null)
            {
                throw new ArgumentException("Tiling requires split criteria!");
            }
        }

        SplitDim[] GetSplitDims(MeshOperator meshOperator, BoundingBox bounds)
        {
            if(Options.treeType == TreeType.N_ary)
            {
                return Options.splitDims;
            } else
            {
                //Points may not extend to all edges of given bounds
                BoundingBox trueBounds = meshOperator.Clip(bounds).Bounds();
                double xRange = trueBounds.Max.X - trueBounds.Min.X;
                double yRange = trueBounds.Max.Y - trueBounds.Min.Y;
                double zRange = trueBounds.Max.Z - trueBounds.Min.Z;
                if(xRange >= yRange && xRange >= zRange)
                {
                    return new SplitDim[] { SplitDim.X };
                } else if (yRange >= zRange)
                {
                    return new SplitDim[] { SplitDim.Y };
                } else
                {
                    return new SplitDim[] { SplitDim.Z };
                }
            }
        }

        /// <summary>
        /// Create a list of children by splitting each tile node along the dimension given, children will know in what direction they have neighbors
        /// </summary>
        /// <param name="tileNodes"></param>
        /// <param name="dim"></param>
        /// <returns></returns>
        IEnumerable<TileNode> BisectBoxes(IEnumerable<TileNode> TileNodes, SplitDim dim)
        {

            foreach (TileNode tn in TileNodes)
            {
                if (dim == SplitDim.X)
                {
                    BoundingBox b1 = new BoundingBox(new Vector3(tn.Bounds.Min.X, tn.Bounds.Min.Y, tn.Bounds.Min.Z), new Vector3((tn.Bounds.Min.X + tn.Bounds.Max.X) / 2.0, tn.Bounds.Max.Y, tn.Bounds.Max.Z));
                    BoundingBox b2 = new BoundingBox(new Vector3((tn.Bounds.Min.X + tn.Bounds.Max.X) / 2.0, tn.Bounds.Min.Y, tn.Bounds.Min.Z), new Vector3(tn.Bounds.Max.X, tn.Bounds.Max.Y, tn.Bounds.Max.Z));
                    TileNode tn1 = new TileNode(tn, b1);
                    TileNode tn2 = new TileNode(tn, b2);
                    tn1.HasPosXNeighbor = true;
                    tn2.HasNegXNeighbor = true;
                    yield return tn1;
                    yield return tn2;
                }
                else if (dim == SplitDim.Y)
                {
                    BoundingBox b1 = new BoundingBox(new Vector3(tn.Bounds.Min.X, tn.Bounds.Min.Y, tn.Bounds.Min.Z), new Vector3(tn.Bounds.Max.X, (tn.Bounds.Min.Y + tn.Bounds.Max.Y) / 2, tn.Bounds.Max.Z));
                    BoundingBox b2 = new BoundingBox(new Vector3(tn.Bounds.Min.X, (tn.Bounds.Min.Y + tn.Bounds.Max.Y) / 2, tn.Bounds.Min.Z), new Vector3(tn.Bounds.Max.X, tn.Bounds.Max.Y, tn.Bounds.Max.Z));
                    TileNode tn1 = new TileNode(tn, b1);
                    TileNode tn2 = new TileNode(tn, b2);
                    tn1.HasPosYNeighbor = true;
                    tn2.HasNegYNeighbor = true;
                    yield return tn1;
                    yield return tn2;
                }
                else
                {
                    BoundingBox b1 = new BoundingBox(new Vector3(tn.Bounds.Min.X, tn.Bounds.Min.Y, tn.Bounds.Min.Z), new Vector3(tn.Bounds.Max.X, tn.Bounds.Max.Y, (tn.Bounds.Min.Z + tn.Bounds.Max.Z) / 2));
                    BoundingBox b2 = new BoundingBox(new Vector3(tn.Bounds.Min.X, tn.Bounds.Min.Y, (tn.Bounds.Min.Z + tn.Bounds.Max.Z) / 2), new Vector3(tn.Bounds.Max.X, tn.Bounds.Max.Y, tn.Bounds.Max.Z));
                    TileNode tn1 = new TileNode(tn, b1);
                    TileNode tn2 = new TileNode(tn, b2);
                    tn1.HasPosZNeighbor = true;
                    tn2.HasNegZNeighbor = true;
                    yield return tn1;
                    yield return tn2;
                }
            }
        }

        private static double GetCoord(Vertex v, SplitDim dim)
        {
            return GetCoord(v.Position, dim);
        }

        private static double GetCoord(Vector3 v, SplitDim dim)
        {
            if (dim == SplitDim.X)
            {
                return v.X;
            }
            else if (dim == SplitDim.Y)
            {
                return v.Y;
            }
            else
            {
                return v.Z;
            }
        }

        private IEnumerable<TileNode> WeightedBisectBoxes(IEnumerable<TileNode> tileNodes, SplitDim splitDim, Dictionary<SplitDim, List<List<Vertex>>> sortedLists, out Dictionary<SplitDim, List<List<Vertex>>> newSortedLists)
        {         
            if (tileNodes.Count() != sortedLists[splitDim].Count)
            {
                throw new ArgumentException("Missing sorted list(s) for weighted split!");
            }

            List<TileNode> newTileNodes = new List<TileNode>();
            newSortedLists = new Dictionary<SplitDim, List<List<Vertex>>>();
            foreach (SplitDim dim in Options.splitDims)
            {
                newSortedLists.Add(dim, new List<List<Vertex>>());
            }

            int i = 0;
            foreach (TileNode tn in tileNodes)
            {
                //Compute the split location
                int medianIndex = sortedLists[splitDim][i].Count()/2;
                double splitLoc;
                if (sortedLists[splitDim][i].Count() > 0)
                {
                    splitLoc = GetCoord(sortedLists[splitDim][i][medianIndex], splitDim);
                } else
                {
                    splitLoc = GetCoord((tn.Bounds.Max + tn.Bounds.Min)/2, splitDim);
                }

                //Do the split
                TileNode tn1;
                TileNode tn2;
                if (splitDim == SplitDim.X)
                {
                    BoundingBox b1 = new BoundingBox(new Vector3(tn.Bounds.Min.X, tn.Bounds.Min.Y, tn.Bounds.Min.Z), new Vector3(splitLoc, tn.Bounds.Max.Y, tn.Bounds.Max.Z));
                    BoundingBox b2 = new BoundingBox(new Vector3(splitLoc, tn.Bounds.Min.Y, tn.Bounds.Min.Z), new Vector3(tn.Bounds.Max.X, tn.Bounds.Max.Y, tn.Bounds.Max.Z));
                    tn1 = new TileNode(tn, b1);
                    tn2 = new TileNode(tn, b2);
                    tn1.HasPosXNeighbor = true;
                    tn2.HasNegXNeighbor = true;
                }
                else if (splitDim == SplitDim.Y)
                {
                    BoundingBox b1 = new BoundingBox(new Vector3(tn.Bounds.Min.X, tn.Bounds.Min.Y, tn.Bounds.Min.Z), new Vector3(tn.Bounds.Max.X, splitLoc, tn.Bounds.Max.Z));
                    BoundingBox b2 = new BoundingBox(new Vector3(tn.Bounds.Min.X, splitLoc, tn.Bounds.Min.Z), new Vector3(tn.Bounds.Max.X, tn.Bounds.Max.Y, tn.Bounds.Max.Z));
                    tn1 = new TileNode(tn, b1);
                    tn2 = new TileNode(tn, b2);
                    tn1.HasPosYNeighbor = true;
                    tn2.HasNegYNeighbor = true;
                }
                else
                {
                    BoundingBox b1 = new BoundingBox(new Vector3(tn.Bounds.Min.X, tn.Bounds.Min.Y, tn.Bounds.Min.Z), new Vector3(tn.Bounds.Max.X, tn.Bounds.Max.Y, splitLoc));
                    BoundingBox b2 = new BoundingBox(new Vector3(tn.Bounds.Min.X, tn.Bounds.Min.Y, splitLoc), new Vector3(tn.Bounds.Max.X, tn.Bounds.Max.Y, tn.Bounds.Max.Z));
                    tn1 = new TileNode(tn, b1);
                    tn2 = new TileNode(tn, b2);
                    tn1.HasPosZNeighbor = true;
                    tn2.HasNegZNeighbor = true;
                }
                newTileNodes.Add(tn1);
                newTileNodes.Add(tn2);

                //Create new sorted lists to pass down
                int baseIndex = newTileNodes.Count - 2;
                foreach (SplitDim dim in Options.splitDims)
                {
                    List<Vertex> list1 = new List<Vertex>();
                    List<Vertex> list2 = new List<Vertex>();
                    foreach (Vertex v in sortedLists[dim][i])
                    {
                        if (newTileNodes[baseIndex].Bounds.Contains(v.Position) == ContainmentType.Contains || newTileNodes[baseIndex].Bounds.Contains(v.Position) == ContainmentType.Intersects)
                        {
                            list1.Add(v);
                        }
                        if (newTileNodes[baseIndex + 1].Bounds.Contains(v.Position) == ContainmentType.Contains || newTileNodes[baseIndex + 1].Bounds.Contains(v.Position) == ContainmentType.Intersects)
                        {
                            list2.Add(v);
                        }
                    }
                    newSortedLists[dim].Add(list1);
                    newSortedLists[dim].Add(list2);                  
                }
                i++;
            }
            return newTileNodes;
        }

        private int vertComp(Vertex a, Vertex b, SplitDim dim)
        {
            return GetCoord(a, dim).CompareTo(GetCoord(b, dim));
        }

        public TileNode SubDivide(MeshOperator meshOperator)
        {
            TileNode root = new TileNode(null, meshOperator.Bounds);
            root.id = "0";
            SubDivide(meshOperator, root);
            return root;
        }

        void SubDivide(MeshOperator meshOperator, TileNode parent, Dictionary<SplitDim, List<List<Vertex>>> sortedLists = null)
        {
            if (meshOperator == null)
            {
                throw new NullReferenceException("Null mesh operator passed to Subdivide");
            }

            //For weighted splits compute initial sorted vertex lists in each dimension
            if (this.Options.splitType == SplitType.Weighted && sortedLists == null)
            {
                sortedLists = new Dictionary<SplitDim, List<List<Vertex>>>();
                foreach(SplitDim dim in Options.splitDims)
                {
                    var vertices = new List<Vertex>(meshOperator.Vertices);
                    vertices.Sort((a, b) => vertComp(a, b, dim));
                    sortedLists.Add(dim, new List<List<Vertex>> { vertices });
                }
            }
            
            //If I still need to split
            if(this.Options.tileSplitCriteria.ShouldSplit(meshOperator, parent.Bounds))
            {
                //get split dims
                SplitDim[] splitDims = GetSplitDims(meshOperator, parent.Bounds);

                //do the split in each dim (weighted or unweighted)
                List<TileNode> children = new List<TileNode> { parent };
                foreach (SplitDim dim in splitDims)
                {
                    if (Options.splitType == SplitType.Unweighted) {
                        children = BisectBoxes(children, dim).ToList();
                    } else
                    {
                        Dictionary<SplitDim, List<List<Vertex>>> newSortedLists;
                        children = WeightedBisectBoxes(children, dim, sortedLists, out newSortedLists).ToList();
                        sortedLists = newSortedLists;
                    }
                }

                parent.Children = children;
                int i = 0;
                foreach(var child in parent.Children)
                {
                    child.id = parent.id + i;
                    child.Parent = parent;
                    i++;
                }

                //recurse
                i = 0;
                foreach (TileNode tn in parent.Children)
                {                   
                    if (Options.splitType == SplitType.Weighted)
                    {
                        var tnSortedLists = new Dictionary<SplitDim, List<List<Vertex>>>();
                        foreach (SplitDim dim in Options.splitDims)
                        {
                            tnSortedLists.Add(dim, new List<List<Vertex>> { sortedLists[dim][i] });
                        }
                        SubDivide(meshOperator, tn, tnSortedLists);
                        i++;
                    } else
                    {
                        SubDivide(meshOperator, tn);
                    }
                }
            }
        }

        /*
        public IEnumerable<Mesh> Create(MeshOperator meshOperator)
        {
            System.Collections.Concurrent.BlockingCollection<Mesh> meshes = new System.Collections.Concurrent.BlockingCollection<Mesh>();
            var boxes = SubDivide(meshOperator, meshOperator.Bounds);

            Serial.ForEach(boxes, tileBounds =>
            {
                double influenceScale = 1;
                double scaleChange = 2;

                int maxIterations = 50;
                int iterations = 0;

                //Grab enough points
                while (!Options.lowerInfluenceRegion.ShouldSplit(meshOperator, BoundingBoxExtensions.Scale(tileBounds, influenceScale)) && iterations <= maxIterations)
                {
                    influenceScale *= scaleChange;
                    iterations++;
                }
                scaleChange = influenceScale/2;
                
                //Refine box to be within limiting criteria
                bool tooSmall = false;
                bool tooLarge = true;
                while((tooSmall || tooLarge) && iterations < maxIterations)
                {
                    if(tooSmall && tooLarge)
                    {
                        throw new Exception("Detected bad influence criteria!");
                    }
                    iterations++;
                    scaleChange = scaleChange / 2;
                    if(tooSmall)
                    {
                        influenceScale += scaleChange;
                    } else
                    {
                        influenceScale -= scaleChange;
                    }
                    tooSmall = !Options.lowerInfluenceRegion.ShouldSplit(meshOperator, BoundingBoxExtensions.Scale(tileBounds, influenceScale));
                    tooLarge = Options.upperInfluenceRegion.ShouldSplit(meshOperator, BoundingBoxExtensions.Scale(tileBounds, influenceScale));
                }

                BoundingBox influenceRegion = BoundingBoxExtensions.Scale(tileBounds, influenceScale);
                Mesh temp = meshOperator.Clip(influenceRegion);

                if(iterations >= maxIterations)
                {
                    Console.WriteLine("WARNING: Failed to converge!");
                }
                Console.WriteLine("Processing tile with " + meshOperator.CountVertices(tileBounds) + " vertices using region with " + meshOperator.CountVertices(influenceRegion));
                Console.WriteLine("After clipping mesh has " + temp.Vertices.Count + " vertices");
                Console.WriteLine("Scale was " + influenceScale);
                Console.WriteLine("Iterations: " + iterations);

                //temp.GenerateVertexNormals();
                temp.Clean();
                try {
                    var res = CreateTile(temp, tileBounds);
                    res.Clean();
                    if (res.Vertices.Count > 0)
                    {
                        meshes.Add(res);
                    }
                }
                catch
                {
                    Console.WriteLine("FAILED building mesh: " + tileBounds.ToString() + "\n");
                    return;
                }
               
                Console.WriteLine("Finished building mesh: " + tileBounds.ToString() + "\n");             
            });
            //meshes.Add(box);
            return meshes;
        }
        */

        static Mesh CreateTile(Mesh points, BoundingBox bounds)
        {
            if (points.Vertices.Count == 0)
            {
                return null;
            }
            if (!points.HasNormals)
            {
                points.GenerateVertexNormals();
            }
            points.ClearUVs();
            points.ClearColors();
            points.Clean();
            points = PoissonReconstruction.Reconstruct(points, 30, 6);
            points.Clean();
            MeshOperator op = new MeshOperator(points);
            return op.Clip(bounds);
        }
    }
}
