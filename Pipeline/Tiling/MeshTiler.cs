using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Geometry;
using Microsoft.Xna.Framework;
using OPS.Imaging;
using OPS.Util;

namespace OPS.Pipeline
{
    /// <summary>
    /// Generic alogrithm for generating a set of tiles from a mesh
    /// </summary>
    public class MeshTiler
    {
        MeshOperator meshOperator;
        BoundingBox totalBounds;
        ITilingScheme tilingScheme;
        ITileSplitCriteria splitCriteria;
        public SceneNode Root { get; private set; }

        public delegate void SaveNodeDelegate(SceneNode node);
        public delegate void LoadNodeDelegate(SceneNode node);

        /// <summary>
        /// Create a mesh tiler
        /// </summary>
        /// <param name="meshOperator">Operator built using the mesh we want to tile</param>
        /// <param name="totalBounds">Overal bounds of the tileset, typically the same as the bounds of the mesh being tiled</param>
        /// <param name="tilingScheme">Tiling scheme determines how tiles are subdivided</param>
        /// <param name="splitCriteria">Splitting criteria determines when tiles should be split</param>
        public MeshTiler(MeshOperator meshOperator, BoundingBox totalBounds, ITilingScheme tilingScheme, ITileSplitCriteria splitCriteria)
        {
            this.meshOperator = meshOperator;
            this.totalBounds = totalBounds;
            this.tilingScheme = tilingScheme;
            this.splitCriteria = splitCriteria;
        }
        
        /// <summary>
        /// Helper method that runs all 3 steps in the algorithm
        /// </summary>
        public void Run(LoadNodeDelegate loadNode, SaveNodeDelegate saveNode, IContentProducer leafProducer, IContentProducer parentProducer)
        {
            BuildBoundsTree();
            TryLoadAssetsFromDisk(loadNode);
            BuildMeshes(leafProducer, parentProducer, saveNode);
        }

        /// <summary>
        /// Step 1 in tiling a mesh
        /// Build a tree of nodes based on tiling scheme and splitting criteria
        /// Nodes won't have content yet but their bounds will be determined
        /// </summary>
        public void BuildBoundsTree()
        {            
            // Create root
            SceneNode root = new SceneNode();
            root.Bounds = totalBounds;
            // Breadth first tree traversal
            Queue<SceneNode> tilesToProcess = new Queue<SceneNode>();
            tilesToProcess.Enqueue(root);            
            while (tilesToProcess.Count > 0)
            {
                // Should we split the current node?
                SceneNode curNode = tilesToProcess.Dequeue();
                if (this.splitCriteria.ShouldSplit(meshOperator, curNode.Bounds))
                {                    
                    // If so split it and create new nodes for all of its children
                    foreach (BoundingBox bounds in tilingScheme.Split(meshOperator, curNode.Bounds))
                    {
                        SceneNode child = new SceneNode();
                        curNode.AddChild(child);
                        child.Bounds = bounds;
                        tilesToProcess.Enqueue(child);
                    }
                }
            }
            this.Root = root;
        }
        
        /// <summary>
        /// Step 2 in building tiles
        /// Attempt to load assets for each node from disk
        /// This is useful if computation is interuppted and you want to resume
        /// and re-use assets that have already been built and saved to disk
        /// </summary>
        /// <param name="loadNode"></param>
        public void TryLoadAssetsFromDisk(LoadNodeDelegate loadNode)
        {
            Parallel.ForEach(this.Root.DepthFirstTraverse(), curNode =>
            {
                if (curNode.GetContent<MeshImagePair>() == null)
                {
                    loadNode(curNode);
                }
            });
        }

        /// <summary>
        /// Step 3 in building tiles
        /// Create assets for all leaf and parent nodes
        /// </summary>
        /// <param name="leafProducer"></param>
        /// <param name="parentProducer"></param>
        /// <param name="saveNode"></param>
        public void BuildMeshes(IContentProducer leafProducer, IContentProducer parentProducer, SaveNodeDelegate saveNode)
        {           
            HashSet<SceneNode> nodesToProcess = new HashSet<SceneNode>();
            HashSet<SceneNode> processedNodes = new HashSet<SceneNode>();
            // Get a list of leaf tiles and seed the initial list of tiles to process with them
            foreach (SceneNode leaf in Root.LeafNodes())
            {
                nodesToProcess.Add(leaf);
            }
            while (nodesToProcess.Count > 0)
            {
                // Generate assets for our nodes to process
                Parallel.ForEach(nodesToProcess, curNode =>
                {
                    if (curNode.GetContent<MeshImagePair>() == null)
                    {
                        if (curNode.IsLeaf)
                        {
                            leafProducer.ProduceContent(curNode, this.meshOperator);                           
                        }
                        else
                        {
                            parentProducer.ProduceContent(curNode, this.meshOperator);
                        }
                        saveNode(curNode);
                    }
                });
                // Record nodes we just generated as processed
                foreach (SceneNode node in nodesToProcess)
                {
                    processedNodes.Add(node);
                }
                // For each node we just processed check if its parent is ready to be processed in the next loop
                HashSet<SceneNode> nextToProcess = new HashSet<SceneNode>();
                foreach (SceneNode curNode in nodesToProcess)
                {
                    // Skip the root node and any nodes we have already marked to process
                    if (curNode.Parent != null && !nextToProcess.Contains(curNode.Parent))
                    {
                        // If all the parent's nodes have content it can be processed
                        bool canProcess = curNode.Parent.Children.All(c => processedNodes.Contains(c));
                        if (canProcess)
                        {
                            nextToProcess.Add(curNode.Parent);
                        }
                    }
                }
                nodesToProcess = nextToProcess;
            }
        }
    }
}
