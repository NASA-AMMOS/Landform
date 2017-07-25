using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.MathExtensions;

namespace OPS.Alignment
{
    class GTMVertex
    {
        public readonly int Index;
        public readonly ImageFeature Feature;

        public GTMVertex(int index, ImageFeature feature)
        {
            this.Index = index;
            this.Feature = feature;
        }

        // Override for speed
        public override int GetHashCode()
        {
            return this.Index;
        }

        // Override this to only use Index, this way all our hash table lookups will work
        public override bool Equals(Object obj)
        {
            if (obj == null || GetType() != obj.GetType())
                return false;
            GTMVertex p = (GTMVertex)obj;
            return this.Index == p.Index;
        }
    }

    class GTMEdgeRecord
    {
        public readonly GTMVertex Vertex;           // Vertex this recored represents
        public List<GTMVertex> FirstK;              // Orderd closest matches (length K or less)    (O)
        LinkedList<GTMVertex> remainingEdges;       // Ordered closet matches after the first K     (O)
        public HashSet<GTMVertex> InFirstKOf;       // Other vertices which reference this vertex   (I and C)

        public GTMEdgeRecord(GTMVertex vertex, List<GTMVertex> sortedNeighbors, int k)
        {
            this.Vertex = vertex;
            FirstK = new List<GTMVertex>();
            remainingEdges = new LinkedList<GTMVertex>();
            InFirstKOf = new HashSet<GTMVertex>();
            for(int i = 0; i < sortedNeighbors.Count; i++)
            {
                if(i < k)
                {
                    FirstK.Add(sortedNeighbors[i]);
                }
                else
                {
                    remainingEdges.AddLast(sortedNeighbors[i]);
                }
            }
        }

        /// <summary>
        /// Removes a vertex and replaces it with the next closest
        /// Returns the next closest in the list or null if there are no more
        /// </summary>
        /// <param name="vertexToRemove"></param>
        /// <returns></returns>
        public GTMVertex PopVert(GTMVertex vertexToRemove)
        {
            if(remainingEdges.Count == 0)
            {
                FirstK.Remove(vertexToRemove);
                return null;
            }
            var replacement =  remainingEdges.First.Value;
            remainingEdges.RemoveFirst();
            FirstK[FirstK.IndexOf(vertexToRemove)] = replacement;
            return replacement;
        }


        public IEnumerable<GTMVertex> JoinedSet()
        {
            return new HashSet<GTMVertex>(FirstK).Union(InFirstKOf);
        }
    }

    class GTMDistances
    {
        double[,] distances;
        public readonly double Median;

        public GTMDistances(List<GTMVertex> verts)
        {
            List<double> values = new List<double>();
            distances = new double[verts.Count, verts.Count];
            for (int j = 0; j < verts.Count; j++)
            {
                for (int k = j + 1; k < verts.Count; k++)
                {
                    double dist = Vector2.Distance(verts[j].Feature.Location, verts[k].Feature.Location);
                    values.Add(dist);
                    // L2 Norm
                    distances[verts[j].Index, verts[k].Index] = dist;
                    distances[verts[k].Index, verts[j].Index] = dist;
                }
            }
            Median = MathExtensions.Median.MedianOfMedians(values, values.Count / 2);
        }

        public double Distance(GTMVertex v1, GTMVertex v2)
        {
            return distances[v1.Index, v2.Index];
        }
    }

    class GTMMatrix
    {
        Dictionary<GTMVertex, GTMEdgeRecord> records;
        int K;  

        public GTMMatrix(List<GTMVertex> verts, int k)
        {
            this.K = k;
            records = new Dictionary<GTMVertex, GTMEdgeRecord>();
            GTMDistances distances = new GTMDistances(verts);
            // Create records
            foreach (GTMVertex curVert in verts)
            {                
                // For each vertex, find all other verts with dist under or equal to mean
                var neighbors = verts.Where(v => v != curVert && distances.Distance(curVert, v) <= distances.Median).OrderBy(v => distances.Distance(curVert,v)).ToList();
                if(neighbors.Count < k)
                {
                    continue;
                }
                GTMEdgeRecord record = new GTMEdgeRecord(curVert, neighbors, k);
                records.Add(curVert, record);
            }
            // Update each record with the vertices that it is referenced by in the first k neighbors
            foreach(GTMEdgeRecord record in records.Values)
            {
                foreach(GTMVertex vert in record.FirstK)
                {
                    records[vert].FirstK.Add(record.Vertex);
                }
            }
        }

        public void RemoveOutlier(GTMVertex vertToRemove)
        {
            // (iii) remove outlier from first k columns of O and reconnect it updating the respective entries
            // For each vertex connected to the vertex we want to remove
            foreach(GTMVertex connectedVert in records[vertToRemove].InFirstKOf)
            {
                // Remove the vertex from its nearest K neighbors.              
                GTMVertex replacement = records[connectedVert].PopVert(vertToRemove);
                /// Check the replacement vertex to make sure it hasn't also been removed. 
                /// If it has replace it with the next option.  Stop if we run out of verts.   
                while (replacement != null && !records.ContainsKey(replacement))
                { 
                    replacement = records[connectedVert].PopVert(replacement);
                }
                // Replacement vert is now referenced by connectedVert
                records[replacement].FirstK.Add(connectedVert);
            }

            // (i) remove all occurences of outlier from I (i.e. all records that reference this vertex)
            foreach(var record in records.Values)
            {
                record.InFirstKOf.Remove(vertToRemove);
            }

            // (ii) remove the outlier from O
            records.Remove(vertToRemove);
        }

        public GTMVertex FindOutlier(GTMMatrix other)
        {
            int maxcount = -1;
            GTMVertex worstVertex = null;
            foreach(GTMVertex vertex in records.Keys)
            {
                var a = this.records[vertex].JoinedSet();
                var b = other.records[vertex].JoinedSet();
                int count = a.Except(b).Union(b.Except(a)).Count();
                if(count > maxcount)
                {
                    maxcount = count;
                    worstVertex = vertex;
                }
            }
            return worstVertex;
        }

        public bool GraphEqual(GTMMatrix other)
        {
            if(other.records.Count != this.records.Count)
            {
                // this is unexpected
                return false;
            }
            // Could  have an edge bug case here, might want to think about checkin the other direction too           
            foreach(var record in this.records.Values)
            {
                HashSet<GTMVertex> a = new HashSet<GTMVertex>(record.FirstK);
                HashSet<GTMVertex> b = new HashSet<GTMVertex>(other.records[record.Vertex].FirstK);
                if (!a.SetEquals(b))
                {
                    return false;
                }
            }
            return true;
        }
    }

    public class GTMNuevo
    {
        GTMMatrix dataGraph;
        GTMMatrix modelGraph;

        public GTMNuevo(ImagePairCorrespondence matches, int k)
        {
            KeyValuePair<int, int>[] pairs = matches.DataToModel;
            ImageFeature[] modelFeat = matches.ModelFeatures;
            ImageFeature[] dataFeat = matches.DataFeatures;
            
            // Create data and model verts and add to graphs           
            List<GTMVertex> dataVerts = new List<GTMVertex>();
            List<GTMVertex> modelVerts = new List<GTMVertex>();
            int i = 0;
            foreach(var pair in pairs)
            {
                dataVerts.Add(new GTMVertex(i, dataFeat[pair.Key]));
                modelVerts.Add(new GTMVertex(i, modelFeat[pair.Value]));
                i++;
            }
            dataGraph = new GTMMatrix(dataVerts, k);
            modelGraph = new GTMMatrix(modelVerts, k);

        }

        internal ImagePairCorrespondence Filter()
        {
            GTMVertex outlier;
            int counter = 0;

            while (!modelGraph.GraphEqual(dataGraph))
            {
                counter++;
                outlier = modelGraph.FindOutlier(dataGraph);
                modelGraph.RemoveOutlier(outlier);
                dataGraph.RemoveOutlier(outlier);
            }

            // TODO: remove disconnected vertices
            // TODO: finsih method
            return null;
        }

    }


}
