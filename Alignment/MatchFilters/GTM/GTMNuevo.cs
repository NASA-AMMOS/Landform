using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.MathExtensions;

namespace OPS.Alignment
{
    class GTMNode
    {
        public readonly int Index;
        public readonly ImageFeature Feature;
        public HashSet<GTMNode> Neighbors;              // Nodes that are my neighbors
        public HashSet<GTMNode> NeighborOf;             // Nodes that I am a neighbor of
        public LinkedList<GTMNode> BackupNeighors;      // Sorted list of nearest neighbors
        
        public GTMNode(int index, ImageFeature feature)
        {
            this.Index = index;
            this.Feature = feature;
            Neighbors = new HashSet<GTMNode>();
            NeighborOf = new HashSet<GTMNode>();
            BackupNeighors = new LinkedList<GTMNode>();
        }

        /// <summary>
        /// Given a list of neighbors sorted by ascending distance from this node
        /// Add them to this nodes Neighbor list and backup neighbor list.
        /// </summary>
        /// <param name="sortedNearestNeighbors"></param>
        /// <param name="k"></param>
        public void AddNeighbors(List<GTMNode> sortedNearestNeighbors, int k)
        {
            for (int i = 0; i < sortedNearestNeighbors.Count; i++)
            {
                // If this is one of the closest k nodes
                if (i < k)
                {
                    Neighbors.Add(sortedNearestNeighbors[i]);        // Add other node as my neighbor 
                    sortedNearestNeighbors[i].NeighborOf.Add(this);  // Add myself to the list of nodes that refernce the other node as its neighbor
                }
                else
                {
                    BackupNeighors.AddLast(sortedNearestNeighbors[i]);
                }
            }
        }
        
        /// <summary>
        /// Given a dictioanry of all nodes in the graph, remove this node
        /// from both the graph and the set
        /// </summary>
        /// <param name="allNodes"></param>
        public void Remove(Dictionary<int, GTMNode> graphNodes)
        {
            // (ii) remove outlier from O
            graphNodes.Remove(this.Index);
            // (i) remove all occurence of outlier from I 
            // (iii) remove outlire from the first k columns of O and reconect it updating the respective entries
            foreach(GTMNode node in this.Neighbors)
            {
                node.RemoveNeighbor(graphNodes, this);
            }
        }

        void RemoveNeighbor(Dictionary<int, GTMNode> graphNodes, GTMNode nodeToRemove)
        {
            // Remove myself from being a neighbor of node being removed         
            nodeToRemove.NeighborOf.Remove(this);
            // Remove node from my list of neighbors
            this.Neighbors.Remove(nodeToRemove);
            // Replace removed node with the next closest neighbor in my neighbor list that is still in the graph
            while (this.BackupNeighors.Count > 0)
            {
                GTMNode replacement = this.BackupNeighors.First.Value;
                this.BackupNeighors.RemoveFirst();
                // Is this backup node still int he graph or has it been removed
                if(graphNodes.ContainsKey(replacement.Index))
                {
                    // Add as my neighbor
                    this.Neighbors.Add(replacement);
                    // Register myself as a neighbor to the replacement node
                    replacement.NeighborOf.Add(this);
                    break;
                }
            }
        }

        public IEnumerable<GTMNode> JoinedSet()
        {
            var r = new HashSet<GTMNode>(Neighbors);
            r.UnionWith(NeighborOf);
            return r;
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
            GTMNode p = (GTMNode)obj;
            return this.Index == p.Index;
        }
    }

   

    class GTMDistances
    {
        double[,] distances;
        public readonly double Median;

        public GTMDistances(List<GTMNode> nodes)
        {
            List<double> values = new List<double>();
            distances = new double[nodes.Count, nodes.Count];
            for (int j = 0; j < nodes.Count; j++)
            {
                for (int k = j + 1; k < nodes.Count; k++)
                {
                    double dist = Vector2.Distance(nodes[j].Feature.Location, nodes[k].Feature.Location);
                    values.Add(dist);
                    // L2 Norm
                    distances[nodes[j].Index, nodes[k].Index] = dist;
                    distances[nodes[k].Index, nodes[j].Index] = dist;
                }
            }
            Median = MathExtensions.Median.MedianOfMedians(values, values.Count / 2);
        }

        public double Distance(GTMNode n1, GTMNode n2)
        {
            return distances[n1.Index, n2.Index];
        }
    }

    class GTMGraphNice
    {
        /// <summary>
        /// Set of all nodes in the graph
        /// </summary>
        Dictionary<int, GTMNode> graphNodes; 
        
        int K;  

        public GTMGraphNice(List<GTMNode> nodes, int k)
        {
            this.K = k;
            this.graphNodes = new Dictionary<int, GTMNode>();
            GTMDistances distances = new GTMDistances(nodes);
            // Add a nearest neighbors list to each node
            foreach (GTMNode curNode in nodes)
            {
                // Add node to graph
                graphNodes.Add(curNode.Index, curNode);
                // For each node, find all other nodes with dist <= to mean
                var neighbors = nodes.Where(n => n != curNode && distances.Distance(curNode, n) <= distances.Median).OrderBy(n => distances.Distance(curNode,n)).ToList();
                if(neighbors.Count < k)
                {
                    continue;
                }
                curNode.AddNeighbors(neighbors, k);
            }
        }
        
        public int FindOutlier(GTMGraphNice other)
        {
            int maxcount = -1;
            int worstNodeIndex = -1;
            foreach(GTMNode myNode in this.graphNodes.Values)
            {
                GTMNode otherNode = other.graphNodes[myNode.Index]; 
                var a = myNode.JoinedSet();
                var b = otherNode.JoinedSet();
                int count = a.Except(b).Union(b.Except(a)).Count();
                if(count > maxcount)
                {
                    maxcount = count;
                    worstNodeIndex = myNode.Index;
                }
            }
            return worstNodeIndex;
        }

        public void RemoveOutlier(int index)
        {
            this.graphNodes[index].Remove(this.graphNodes);
        }

        public bool GraphEqual(GTMGraphNice other)
        {
            if(other.graphNodes.Count != this.graphNodes.Count)
            {
                // this is unexpected in our algorithm
                throw new Exception("Cannot compare GTM graphs of different sizes");
            }
            foreach(int index in this.graphNodes.Keys)
            {
                HashSet<GTMNode> a = new HashSet<GTMNode>(this.graphNodes[index].Neighbors);
                HashSet<GTMNode> b = new HashSet<GTMNode>(other.graphNodes[index].Neighbors);
                if (!a.SetEquals(b))
                {
                    return false;
                }
            }
            return true;
        }

        public void RemoveDisconnected()
        {
            // Remove all nodes that don't have neighbors or aren't referenced as a neighbor
            this.graphNodes = graphNodes.Where(pair => pair.Value.Neighbors.Count == 0 && pair.Value.NeighborOf.Count == 0).ToDictionary(pair => pair.Key, pair => pair.Value);
        }

        public IEnumerable<ImageFeature> OrderedFeatures()
        {
            return this.graphNodes.Values.OrderBy(n => n.Index).Select(n => n.Feature);
        }

        public int Count
        {
            get { return this.graphNodes.Count;  }
        }
    }

    public class GTMNuevo
    {
        GTMGraphNice dataGraph;
        GTMGraphNice modelGraph;
        ImagePairCorrespondence matches;

        public GTMNuevo(ImagePairCorrespondence matches, int k)
        {
            this.matches = matches;
            KeyValuePair<int, int>[] pairs = matches.DataToModel;
            ImageFeature[] modelFeat = matches.ModelFeatures;
            ImageFeature[] dataFeat = matches.DataFeatures;
            
            // Create data and model verts and add to graphs           
            List<GTMNode> dataNodes = new List<GTMNode>();
            List<GTMNode> modelNodes = new List<GTMNode>();
            int i = 0;
            foreach(var pair in pairs)
            {
                dataNodes.Add(new GTMNode(i, dataFeat[pair.Key]));
                modelNodes.Add(new GTMNode(i, modelFeat[pair.Value]));
                i++;
            }
            dataGraph = new GTMGraphNice(dataNodes, k);
            modelGraph = new GTMGraphNice(modelNodes, k);

        }

        internal ImagePairCorrespondence Filter()
        {
              int counter = 0;

            while (!modelGraph.GraphEqual(dataGraph))
            {
                counter++;
                int outlier = modelGraph.FindOutlier(dataGraph);
                modelGraph.RemoveOutlier(outlier);
                dataGraph.RemoveOutlier(outlier);
            }
            modelGraph.RemoveDisconnected();
            dataGraph.RemoveDisconnected();

            // Both graphs are the same size and we return the features ordered by index so we just need a one-to-one dataToModel array
            List<int> dataToModel = new List<int>();
            for(int i = 0; i < modelGraph.Count; i++)
            {
                dataToModel.Add(i);
            }
            return new ImagePairCorrespondence(matches.ModelImage, matches.DataImage, modelGraph.OrderedFeatures(), dataGraph.OrderedFeatures(), dataToModel);
            
        }

    }


}
