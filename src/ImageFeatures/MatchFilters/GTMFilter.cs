using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace JPLOPS.ImageFeatures
{
    /// <summary>
    /// Represents a node in a GTM graph
    /// </summary>
    class GTMNode
    {
        public readonly int Index;
        public readonly int FeatureIndex;
        public readonly ImageFeature Feature;
        public HashSet<GTMNode> Neighbors;              // Nodes that are my neighbors
        public HashSet<GTMNode> NeighborOf;             // Nodes that I am a neighbor of
        public LinkedList<GTMNode> BackupNeighors;      // Sorted list of nearest neighbors

        public HashSet<GTMNode> AllEdges;
        
        public GTMNode(int index, ImageFeature feature, int featureIndex)
        {
            Index = index;
            Feature = feature;
            FeatureIndex = featureIndex;
            Neighbors = new HashSet<GTMNode>();
            NeighborOf = new HashSet<GTMNode>();
            BackupNeighors = new LinkedList<GTMNode>();
            AllEdges = new HashSet<GTMNode>();
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
                GTMNode neighbor = sortedNearestNeighbors[i];
                // If this is one of the closest k nodes
                if (i < k)
                {
                    Neighbors.Add(neighbor);        // Add other node as my neighbor 
                    neighbor.NeighborOf.Add(this);  // Add myself to the list of nodes that refernce the other node as its neighbor

                    AllEdges.Add(neighbor);
                    neighbor.AllEdges.Add(this);
                }
                else
                {
                    BackupNeighors.AddLast(neighbor);
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
            // Remove myself from the neighborsof lists of my neighbors                         
            foreach(GTMNode n in this.Neighbors)
            {
                n.NeighborOf.Remove(this);
                n.AllEdges.Remove(this);
            }
            // Remove me from the neighbors list of things I am a neighbor to
            foreach (GTMNode n in this.NeighborOf)
            {
                n.RemoveNeighbor(graphNodes, this);
            }
            this.NeighborOf.Clear();
        }

        /// <summary>
        /// Remove target node from Neighbors list and replace
        /// Does not update nodeToRemove.NeighborOf
        /// </summary>
        /// <param name="graphNodes"></param>
        /// <param name="nodeToRemove"></param>
        void RemoveNeighbor(Dictionary<int, GTMNode> graphNodes, GTMNode nodeToRemove)
        {
            // Remove node from my list of neighbors
            this.Neighbors.Remove(nodeToRemove);
            this.AllEdges.Remove(nodeToRemove);
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
                    this.AllEdges.Add(replacement);
                    // Register myself as a neighbor to the replacement node
                    replacement.NeighborOf.Add(this);
                    replacement.AllEdges.Add(this);
                    break;
                }
            }
        }

        // Override for speed
        public override int GetHashCode()
        {
            return this.Index;
        }
    } 

    /// <summary>
    /// Pre-computes distances between a set of nodes
    /// </summary>
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
            values.Sort();
            Median = values[values.Count / 2];
        }
        
        /// <summary>
        /// Return the distance between the two nodes, either order is fine
        /// </summary>
        /// <param name="n1"></param>
        /// <param name="n2"></param>
        /// <returns></returns>
        public double Distance(GTMNode n1, GTMNode n2)
        {
            return distances[n1.Index, n2.Index];
        }
    }

    /// <summary>
    /// Class represents a GTM Graph
    /// </summary>
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
                // For each node, find all other nodes with dist <= to mean, and distance greater than 0
                List<GTMNode> potentialNeighbors = new List<GTMNode>();
                foreach(var n in nodes)
                {
                    double d = distances.Distance(curNode, n);
                    if (n != curNode && d != 0 && d <= distances.Median)
                    {
                        potentialNeighbors.Add(n);
                    }
                }
                // Sort by proximity
                potentialNeighbors = potentialNeighbors.OrderBy(n => distances.Distance(curNode, n)).ToList();
                // Only create edges for this node if there are at least k neighbors
                if (potentialNeighbors.Count < k)
                {
                    continue;
                }
                curNode.AddNeighbors(potentialNeighbors, k);
            }
        }

        /// <summary>
        /// Finds the index of the next outlier to remove
        /// </summary>
        /// <param name="other"></param>
        /// <returns></returns>
        public int FindOutlier(GTMGraphNice other)
        {
            int maxcount = -1;
            int worstNodeIndex = -1;
            foreach(GTMNode myNode in this.graphNodes.Values)
            {
                GTMNode otherNode = other.graphNodes[myNode.Index]; 
                var a = myNode.AllEdges.Select(n => n.Index);
                var b = otherNode.AllEdges.Select(n => n.Index);
                // Compute xor of the sets
                var differences = a.Except(b).Union(b.Except(a)).ToArray();
                int count = differences.Length;
                if(count > maxcount)
                {
                    maxcount = count;
                    worstNodeIndex = myNode.Index;
                }
            }
            if(worstNodeIndex == -1)
            {
                throw new Exception("No outlier found, did you check if graphs were equal before calling FindOutlier?");
            }
            return worstNodeIndex;
        }

        /// <summary>
        /// Removes a node from the graph
        /// </summary>
        /// <param name="index"></param>
        public void RemoveNode(int index)
        {
            GTMNode nodeToRemove = this.graphNodes[index];
            // (ii) remove outlier from O
            graphNodes.Remove(index);
            // (i) remove all occurence of outlier from I 
            // (iii) remove outlier from the first k columns of O and reconnect it updating the respective entries
            nodeToRemove.Remove(this.graphNodes);
        }

        /// <summary>
        /// Returns true if two graphs are equal
        /// </summary>
        /// <param name="other"></param>
        /// <returns></returns>
        public bool GraphEqual(GTMGraphNice other)
        {
            if(other.graphNodes.Count != this.graphNodes.Count)
            {
                // this is unexpected in our algorithm
                throw new Exception("Cannot compare GTM graphs of different sizes");
            }
            foreach(int index in this.graphNodes.Keys)
            {
                HashSet<int> a = new HashSet<int>(this.graphNodes[index].AllEdges.Select(n => n.Index));
                HashSet<int> b = new HashSet<int>(other.graphNodes[index].AllEdges.Select(n => n.Index));
                if (!a.SetEquals(b))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Removes any nodes that aren't connected to another node
        /// </summary>
        public void RemoveDisconnected()
        {
            // Remove all nodes that don't have neighbors or aren't referenced as a neighbor
            this.graphNodes = graphNodes.Where(pair => pair.Value.AllEdges.Count() != 0).ToDictionary(pair => pair.Key, pair => pair.Value);
        }

        /// <summary>
        /// Return features from this graph ordered by node index
        /// </summary>
        /// <returns></returns>
        public IEnumerable<ImageFeature> OrderedFeatures()
        {
            return this.graphNodes.Values.OrderBy(n => n.Index).Select(n => n.Feature);
        }

        /// <summary>
        /// Return feature indices from this graph ordered by node index
        /// </summary>
        /// <returns></returns>
        public IEnumerable<int> OrderedFeatureIndices()
        {
            return this.graphNodes.Values.OrderBy(n => n.Index).Select(n => n.FeatureIndex);
        }

        /// <summary>
        /// Return number of nodes in this graph
        /// </summary>
        public int Count
        {
            get { return this.graphNodes.Count;  }
        }

        /// <summary>
        /// Check if this graph is in a valid state
        /// Throw an exception otherwise
        /// </summary>
        public void IsValid()
        {
            foreach(var me in graphNodes.Values)
            {
                ExceptionAssert(me == graphNodes[me.Index]);
                ExceptionAssert(me.Neighbors.Count <= this.K);
                ExceptionAssert(me.Neighbors.Count == this.K || me.BackupNeighors.Count == 0);
                foreach (var neighbor in me.Neighbors)
                {
                    ExceptionAssert(this.graphNodes.ContainsValue(neighbor));
                    ExceptionAssert(neighbor.NeighborOf.Contains(me));
                }
                if (me.BackupNeighors.Count > 1)
                {
                    var cur = me.BackupNeighors.First;
                    var next = cur.Next;
                    if(next != null)
                    {
                        ExceptionAssert(Vector2.Distance(cur.Value.Feature.Location, me.Feature.Location) <= Vector2.Distance(next.Value.Feature.Location, me.Feature.Location));
                    }
                    next = cur;
                }                
                foreach (var neighborOf in me.NeighborOf)
                {
                    ExceptionAssert(this.graphNodes.ContainsValue(neighborOf));
                    ExceptionAssert(neighborOf.Neighbors.Contains(me));
                }
            }
        }

        void ExceptionAssert(bool value)
        {
            if(!value)
            {
                throw new Exception();
            }
        }
    }

    public class GTMFilter : IMatchFilter
    {
        GTMGraphNice dataGraph;
        GTMGraphNice modelGraph;
        int K;

        /// <summary>
        /// Create a GTM filter
        /// </summary>
        /// <param name="k">Paper says to use 5</param>
        public GTMFilter(int k = 5)
        {
            this.K = k;
        }

        public ImagePairCorrespondence Filter(ImageFeature[] modelFeatures, ImageFeature[] dataFeatures,
                                              ImagePairCorrespondence matches)
        {
            // Create data and model verts and add to graphs           
            List<GTMNode> modelNodes = new List<GTMNode>();
            List<GTMNode> dataNodes = new List<GTMNode>();
            for(int i=0; i < matches.DataToModel.Length; i++)
            {
                var pair = matches.DataToModel[i];
                modelNodes.Add(new GTMNode(i, modelFeatures[pair.Value], pair.Value));
                dataNodes.Add(new GTMNode(i, dataFeatures[pair.Key], pair.Key));
            }
            modelGraph = new GTMGraphNice(modelNodes, this.K);
            dataGraph = new GTMGraphNice(dataNodes, this.K);
            // Do filter
            while (!modelGraph.GraphEqual(dataGraph))
            {
                int outlier = modelGraph.FindOutlier(dataGraph);
                modelGraph.RemoveNode(outlier);
                dataGraph.RemoveNode(outlier);
                outlier = modelGraph.FindOutlier(dataGraph);
            }
            modelGraph.RemoveDisconnected();
            dataGraph.RemoveDisconnected();
            if(modelGraph.Count != dataGraph.Count)
            {
                throw new Exception("Unexpected difference in graph sizes");
            }
            // Both graphs are the same size and have a one-to-one correspondence when ordered by index
            var descriptorDistance = new Dictionary<KeyValuePair<int, int>, double>();
            for (int i = 0; i < matches.DataToModel.Length; i++)
            {
                descriptorDistance[matches.DataToModel[i]] = matches.DescriptorDistance[i];
            }
            var dataToModel = Enumerable.Zip(
                dataGraph.OrderedFeatureIndices(),
                modelGraph.OrderedFeatureIndices(),
                (dataIdx, modelIdx) => new FeatureMatch()
                    {
                        DataIndex = dataIdx,
                        ModelIndex = modelIdx,
                        DescriptorDistance = descriptorDistance[new KeyValuePair<int, int>(dataIdx, modelIdx)]
                    });
            return new ImagePairCorrespondence(matches.ModelImageUrl, matches.DataImageUrl, dataToModel,
                                               matches.FundamentalMatrix, matches.BestTransformEstimate);
        }
    }
}
