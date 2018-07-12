using Microsoft.Xna.Framework;
using OPS.Geometry;
using OPS.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Pipeline.Tiling
{
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
        KD
    }

    public struct GenericTilingSchemeOptions
    {
        public ITileSplitCriteria tileSplitCriteria;
        public SplitDim[] splitDims;
        public SplitType splitType;
        public TreeType treeType;
        public double influenceRatio;
    }

    public class TriangleDensityCriteria : ITileSplitCriteria
    {
        int VertexPerTile;

        public TriangleDensityCriteria(int FacePerVertex)
        {
            this.VertexPerTile = FacePerVertex;
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

        IEnumerable<BoundingBox> BisectBoxes(IEnumerable<BoundingBox> boxes, SplitDim dim)
        {

            foreach (BoundingBox box in boxes)
            {
                if (dim == SplitDim.X)
                {
                    yield return new BoundingBox(new Vector3(box.Min.X, box.Min.Y, box.Min.Z), new Vector3((box.Min.X + box.Max.X) / 2.0, box.Max.Y, box.Max.Z));
                    yield return new BoundingBox(new Vector3((box.Min.X + box.Max.X) / 2.0, box.Min.Y, box.Min.Z), new Vector3(box.Max.X, box.Max.Y, box.Max.Z));
                }
                else if (dim == SplitDim.Y)
                {
                    yield return new BoundingBox(new Vector3(box.Min.X, box.Min.Y, box.Min.Z), new Vector3(box.Max.X, (box.Min.Y + box.Max.Y) / 2, box.Max.Z));
                    yield return new BoundingBox(new Vector3(box.Min.X, (box.Min.Y + box.Max.Y) / 2, box.Min.Z), new Vector3(box.Max.X, box.Max.Y, box.Max.Z));
                }
                else
                {
                    yield return new BoundingBox(new Vector3(box.Min.X, box.Min.Y, box.Min.Z), new Vector3(box.Max.X, box.Max.Y, (box.Min.Z + box.Max.Z) / 2));
                    yield return new BoundingBox(new Vector3(box.Min.X, box.Min.Y, (box.Min.Z + box.Max.Z) / 2), new Vector3(box.Max.X, box.Max.Y, box.Max.Z));
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

        private IEnumerable<BoundingBox> WeightedBisectBoxes(IEnumerable<BoundingBox> boxes, SplitDim splitDim, Dictionary<SplitDim, List<List<Vertex>>> sortedLists, out Dictionary<SplitDim, List<List<Vertex>>> newSortedLists)
        {         
            if (boxes.Count() != sortedLists[splitDim].Count)
            {
                throw new ArgumentException("Missing sorted list(s) for weighted split!");
            }

            List<BoundingBox> newBoxes = new List<BoundingBox>();
            newSortedLists = new Dictionary<SplitDim, List<List<Vertex>>>();
            foreach (SplitDim dim in Options.splitDims)
            {
                newSortedLists.Add(dim, new List<List<Vertex>>());
            }

            int i = 0;
            foreach (BoundingBox box in boxes)
            {
                int medianIndex = sortedLists[splitDim][i].Count()/2;
                double splitLoc;
                if (sortedLists[splitDim][i].Count() > 0)
                {
                    splitLoc = GetCoord(sortedLists[splitDim][i][medianIndex], splitDim);
                } else
                {
                    splitLoc = GetCoord((box.Max + box.Min)/2, splitDim);
                }
                
                if (splitDim == SplitDim.X)
                {
                    newBoxes.Add(new BoundingBox(new Vector3(box.Min.X, box.Min.Y, box.Min.Z), new Vector3(splitLoc, box.Max.Y, box.Max.Z)));
                    newBoxes.Add(new BoundingBox(new Vector3(splitLoc, box.Min.Y, box.Min.Z), new Vector3(box.Max.X, box.Max.Y, box.Max.Z)));
                }
                else if (splitDim == SplitDim.Y)
                {
                    newBoxes.Add(new BoundingBox(new Vector3(box.Min.X, box.Min.Y, box.Min.Z), new Vector3(box.Max.X, splitLoc, box.Max.Z)));
                    newBoxes.Add(new BoundingBox(new Vector3(box.Min.X, splitLoc, box.Min.Z), new Vector3(box.Max.X, box.Max.Y, box.Max.Z)));
                }
                else
                {
                    newBoxes.Add(new BoundingBox(new Vector3(box.Min.X, box.Min.Y, box.Min.Z), new Vector3(box.Max.X, box.Max.Y, splitLoc)));
                    newBoxes.Add(new BoundingBox(new Vector3(box.Min.X, box.Min.Y, splitLoc), new Vector3(box.Max.X, box.Max.Y, box.Max.Z)));
                }            
                int baseIndex = newBoxes.Count - 2;
                foreach (SplitDim dim in Options.splitDims)
                {
                    List<Vertex> list1 = new List<Vertex>();
                    List<Vertex> list2 = new List<Vertex>();
                    foreach (Vertex v in sortedLists[dim][i])
                    {
                        if (newBoxes[baseIndex].Contains(v.Position) == ContainmentType.Contains || newBoxes[baseIndex].Contains(v.Position) == ContainmentType.Intersects)
                        {
                            list1.Add(v);
                        }
                        if (newBoxes[baseIndex + 1].Contains(v.Position) == ContainmentType.Contains || newBoxes[baseIndex + 1].Contains(v.Position) == ContainmentType.Intersects)
                        {
                            list2.Add(v);
                        }
                    }
                    newSortedLists[dim].Add(list1);
                    newSortedLists[dim].Add(list2);                  
                }
                i++;
            }
            return newBoxes;
        }

        private int vertComp(Vertex a, Vertex b, SplitDim dim)
        {
            return GetCoord(a, dim).CompareTo(GetCoord(b, dim));
            //old...
            if(dim == SplitDim.X)
            {
                return a.Position.X.CompareTo(b.Position.X);
            } else if(dim == SplitDim.Y)
            {
                return a.Position.Y.CompareTo(b.Position.Y);
            } else
            {
                return a.Position.Z.CompareTo(b.Position.Z);
            }
        }

        public IEnumerable<BoundingBox> SubDivide(MeshOperator meshOperator)
        {
            return SubDivide(meshOperator, meshOperator.Bounds);
        }

        IEnumerable<BoundingBox> SubDivide(MeshOperator meshOperator, BoundingBox bounds, Dictionary<SplitDim, List<List<Vertex>>> sortedLists = null)
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

            List<BoundingBox> leaves = new List<BoundingBox>();
            
            //return if small enough
            if(!this.Options.tileSplitCriteria.ShouldSplit(meshOperator, bounds))
            {
                leaves.Add(bounds);
            } else
            {
                //get split dims
                SplitDim[] splitDims = GetSplitDims(meshOperator, bounds);

                //do the split in each dim (weighted or unweighted)
                List<BoundingBox> boxes = new List<BoundingBox> { bounds };
                foreach (SplitDim dim in splitDims)
                {
                    if (Options.splitType == SplitType.Unweighted) {
                        boxes = BisectBoxes(boxes, dim).ToList();
                    } else
                    {
                        Dictionary<SplitDim, List<List<Vertex>>> newSortedLists;
                        boxes = WeightedBisectBoxes(boxes, dim, sortedLists, out newSortedLists).ToList();
                        sortedLists = newSortedLists;
                    }
                }

                //recurse
                int i = 0;
                foreach (BoundingBox b in boxes)
                {                   
                    if (Options.splitType == SplitType.Weighted)
                    {
                        var bSortedLists = new Dictionary<SplitDim, List<List<Vertex>>>();
                        foreach (SplitDim dim in Options.splitDims)
                        {
                            bSortedLists.Add(dim, new List<List<Vertex>> { sortedLists[dim][i] });
                        }
                        leaves.AddRange(SubDivide(meshOperator, b, bSortedLists));
                        i++;
                    } else
                    {
                        leaves.AddRange(SubDivide(meshOperator, b));
                    }
                }
            }
            return leaves;
        }

        public IEnumerable<Mesh> Create(MeshOperator meshOperator)
        {
            System.Collections.Concurrent.BlockingCollection<Mesh> meshes = new System.Collections.Concurrent.BlockingCollection<Mesh>();
            IEnumerable<BoundingBox> boxes = SubDivide(meshOperator, meshOperator.Bounds);

            Serial.ForEach(boxes, tileBounds =>
            {
                BoundingBox influenceRegion = BoundingBoxExtensions.Scale(tileBounds, this.Options.influenceRatio);
                Mesh temp = meshOperator.Clip(influenceRegion);
                temp.Clean();
                var res = CreateTile(temp, tileBounds);
                res.Clean();
                if(res.Vertices.Count > 0)
                {
                    meshes.Add(res);
                }
                Console.WriteLine("Finished building mesh: " + tileBounds.ToString());
                /*BoundingBox dummyBox = BoundingBoxExtensions.Scale(tileBounds, 0.99);
                Vertex v1 = new Vertex(dummyBox.Min);
                Vertex v2 = new Vertex(dummyBox.Min.X, dummyBox.Max.Y, dummyBox.Min.Z);
                Vertex v3 = new Vertex(dummyBox.Max.X, dummyBox.Min.Y, dummyBox.Min.Z);
                Vertex v4 = new Vertex(dummyBox.Max.X, dummyBox.Max.Y, dummyBox.Min.Z);
                Vertex v5 = new Vertex(dummyBox.Min.X, dummyBox.Min.Y, dummyBox.Max.Z);
                Vertex v6 = new Vertex(dummyBox.Min.X, dummyBox.Max.Y, dummyBox.Max.Z);
                Vertex v7 = new Vertex(dummyBox.Max.X, dummyBox.Min.Y, dummyBox.Max.Z);
                Vertex v8 = new Vertex(dummyBox.Max);
                
                box.Vertices.AddRange(new List<Vertex> { v5, v6, v7, v8 } );
                int i = box.Vertices.Count - 4;
                box.Faces.AddRange(new List<Face> {new Face(i, i+1, i+2), new Face(i+2, i+1, i+3)});             */
            });
            //meshes.Add(box);
            return meshes;
        }

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
            points = PoissonReconstruction.PoissonReconstruct(points, 30);
            points.Clean();
            MeshOperator op = new MeshOperator(points);
            return op.Clip(bounds);
        }
    }
}
