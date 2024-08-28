using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using JPLOPS.Util;
using JPLOPS.MathExtensions;
using JPLOPS.Geometry;
using JPLOPS.Imaging;
using JPLOPS.Pipeline.TilingServer;
using JPLOPS.Pipeline.AlignmentServer;

namespace JPLOPS.Pipeline
{
    public static class SceneNodeTilingExtensions
    {
        public static bool useTextureError;

        public static int numProjectAtlas;
        public static int numUVatlas;
        public static int numHeightmapAtlas;
        public static int numNaiveAtlas;
        public static int numManifoldAtlas;

        public static void SaveMesh(this SceneNode node, string directory, string meshExtension = "ply",
                                    string imageExtension = "jpg")
        {
            meshExtension = "." + meshExtension;
            imageExtension = "." + imageExtension;

            var pair = node.GetComponent<MeshImagePair>();
            Mesh m = pair.Mesh;
            string imgName = null;
            if (pair.Image != null)
            {
                imgName = Path.Combine(directory, node.Name + imageExtension);
                pair.Image.Save<byte>(imgName);
            }
            m.Save(Path.Combine(directory, node.Name + meshExtension), imgName);
        }

        public static int GetTileResolution(Mesh mesh, int maxRes = -1, int minRes = -1, double maxTexelsPerMeter = -1,
                                            bool powerOfTwoTextures = false, Action<string> info = null)
        {
            return GetTileResolution(mesh.SurfaceArea(), maxRes, minRes, maxTexelsPerMeter, powerOfTwoTextures, info);
        }

        public static int GetTileResolution(double meshArea, int maxRes = -1, int minRes = -1,
                                            double maxTexelsPerMeter = -1, bool powerOfTwoTextures = false,
                                            Action<string> info = null)
        {
            if (maxRes == 0)
            {
                return 0;
            }

            if (maxRes < 0)
            {
                maxRes = TilingDefaults.MAX_TILE_RESOLUTION;
            }

            if (minRes < 0)
            {
                minRes = TilingDefaults.MIN_TILE_RESOLUTION;
            }

            minRes = Math.Min(minRes, maxRes);

            if (minRes == maxRes)
            {
                return maxRes;
            }

            int res = maxRes;

            double squareTexelsPerSquareMeter = -1;
            double texelArea = -1;
            if (maxTexelsPerMeter > 0)
            {
                squareTexelsPerSquareMeter = maxTexelsPerMeter * maxTexelsPerMeter;
                texelArea = meshArea * squareTexelsPerSquareMeter;

                res = Math.Max(minRes, (int)Math.Sqrt(texelArea));

                if (powerOfTwoTextures)
                {
                    res = MathE.CeilPowerOf2(res);
                }

                res = Math.Min(maxRes, (int)res);
            }

            if (info != null)
            {
                info(string.Format("computed tile resolution {0}, min {1}, max {2}, max texels/meter {3}, " +
                                   "max square texels/square meter {4}, mesh area {5:F3}m^2, max texels {6}, " +
                                   "res for max texels {7}, power of two required {8}",
                                   res, minRes, maxRes, maxTexelsPerMeter,
                                   Fmt.KMG(squareTexelsPerSquareMeter), meshArea, Fmt.KMG(texelArea),
                                   (int)Math.Sqrt(texelArea), powerOfTwoTextures));
            }

            return res;
        }

        /// <summary>
        /// find all nodes that would be required to build a mesh for a given node
        ///
        /// this is potentially more than just the topological descendants of the node
        /// because typically we cast a wider spatial search which enables better boundary conditions for the mesh
        ///
        /// this method returns all nodes d that meet the following conjunctive criteria
        /// 1) d descends from root
        /// 2) the bounding box of d intersects the search bounds, computed as the bounding box union of our
        ///    children's bounds scaled (generally up) by the given search ratio
        /// 3) d is a leaf or is at or greater than the depth (topological distance) from root as this node's children
        ///
        /// NOTE: as in all the tiling code bounds are all in same coordinate frame (all node Transforms are identity)
        /// </summary>
        public static List<SceneNode> FindNodesRequiredForParent(this SceneNode node, SceneNode root)
        {
            var result = new List<SceneNode>();

            if (node.IsLeaf)
            {
                return result;
            }

            int childDepth = node.GetActualDepth() + 1;

            var searchBounds = BoundingBoxExtensions
                .Union(node.Children
                       .Where(c => node.IsActualChild(c))
                       .Select(c => c.GetComponent<NodeBounds>().Bounds).ToArray())
                .CreateScaled(TilingDefaults.CHILD_BOUNDS_SEARCH_RATIO);

            var stack = new Stack<SceneNode>();

            stack.Push(root);

            while (stack.Count > 0)
            {
                var n = stack.Pop();
                var b = n.GetComponent<NodeBounds>().Bounds;
                var d = n.GetActualDepth();
                if (!b.Intersects(searchBounds))
                {
                    continue;
                }
                if (n.IsLeaf || d >= childDepth)
                {
                    result.Add(n);
                    continue;
                }
                foreach (var c in n.Children)
                {
                    stack.Push(c);
                }
            }
            return result;
        }

        //TilingServer.BuildParent.Process() builds a mini-scene graph with just this parent node as root
        //and all its dependencies as first level descendants
        public static int GetActualDepth(this SceneNode node)
        {
            return node.HasComponent<SceneNodeTilingNode>() ?
                node.GetComponent<SceneNodeTilingNode>().TilingNode.Depth : node.Transform.Depth();
        }

        //TilingServer.BuildParent.Process() builds a mini-scene graph with just this parent node as root
        //and all its dependencies as first level descendants
        public static bool IsActualChild(this SceneNode node, SceneNode other)
        {
            return !other.HasComponent<SceneNodeTilingNode>() ||
                other.GetComponent<SceneNodeTilingNode>().TilingNode.ParentId == node.Name;
        }

        //TilingServer.BuildParent.Process() builds a mini-scene graph with just this parent node as root
        //and all its dependencies as first level descendants
        public static void BuildParentGeometry(this SceneNode node, PipelineCore pipeline, TilingProject project,
                                               Action<string> info = null, Action<string> warn = null,
                                               Action<string> error = null)
        {
            node.BuildParentGeometry(pipeline, project, node, info, warn, error);
        }

        /// <summary>
        /// assumes all nodes below this node have been processed
        /// note that a parent node geometry may depend on more than just its own children
        /// see FindNodesRequiredForParent() for more info
        /// </summary>
        public static void BuildParentGeometry(this SceneNode node, PipelineCore pipeline, TilingProject project,
                                               SceneNode root, Action<string> info = null, Action<string> warn = null,
                                               Action<string> error = null)
        {
            info = info ?? (msg => {});
            warn = warn ?? (msg => {});
            error = error ?? (msg => {});

            var dependencies = FindNodesRequiredForParent(node, root);

            //generally the parent node will already have its bounds defined here
            //however, those may have come from ComputeBounds()
            //which may have been run before meshes were actually available
            //so recompute the parent bounds now that the children's meshes are available
            //because if they were the result of decimation they might have grown a little
            //also remember that TilingServer.BuildParent.Process()
            //builds a mini-scene graph with just this parent node as root
            //and all its dependencies as first level descendants
            //so restrict this to the union of dependencies that are actually children
            var parentBounds =
                BoundingBoxExtensions.Union(dependencies
                                            .Where(dep => node.IsActualChild(dep))
                                            .Select(child => child.GetOrAddComponent<NodeBounds>().Bounds)
                                            .ToArray());
            if (node.HasComponent<NodeBounds>())
            {
                parentBounds = BoundingBoxExtensions.Union(parentBounds, node.GetComponent<NodeBounds>().Bounds);
            }
            node.GetOrAddComponent<NodeBounds>().Bounds = parentBounds;

            var depMeshImagePairs = dependencies
                .Where(n => n.HasComponent<MeshImagePair>())
                .Select(n => n.GetComponent<MeshImagePair>())
                .Where(mip => mip.Mesh != null && mip.Mesh.HasFaces)
                .ToArray();

            var depMeshes = depMeshImagePairs.Select(p => p.Mesh).ToArray();
            bool hasNormals = depMeshes.All(m => m.HasNormals);
            
            info($"merging {depMeshes.Length} meshes to build parent with" + (hasNormals ? "" : "out") + " normals");

            //we want the combined mesh to have normals but not UVs or colors
            //because those attributes would not be compatible with FSSR
            //(and we will need to re-atlas the parent mesh in all cases anyway)
            var combinedMesh = MeshMerge.Merge(hasNormals, false, false, depMeshes);

            //the dependent meshes might have some noise due to decimation
            //and some tiles, particularly those that have only orbital geometry, can be extremely flat
            //leading to thin bounding boxes with faces largely parallel to and nearly coincident with the mesh
            //expand the clipping bounds in the thinnest box direction
            //to avoid clipping highs and lows that might have gotten perturbed
            var clippingBounds = parentBounds;
            var minAxis = clippingBounds.MinAxis(out double minDim);
            var otherDim = Math.Sqrt(clippingBounds.GetFaceAreaPerpendicularToAxis(minAxis));
            if (minDim < 0.5 * otherDim)
            {
                var minDir = BoundingBoxExtensions.GetBoxAxisDirection(minAxis);
                clippingBounds.Max += minDir * TilingDefaults.PARENT_CLIP_BOUNDS_EXPAND_HEIGHT * otherDim;
                clippingBounds.Min -= minDir * TilingDefaults.PARENT_CLIP_BOUNDS_EXPAND_HEIGHT * otherDim;
            }

            //create a copy of the combined child meshes clipped to the actual node bounds
            //we'll use this for three purposes
            //(1) if it's empty, we early out
            //(2) if it's already got few enough faces, we'll use it directly instead of calling ResampleDecimated()
            //(3) if we do call ResampleDecimated() we'll use it to compute geometric error
            //note: Mesh.Clip() calls Mesh.Clean() which calls Mesh.NormalizeNormals()
            var combinedClipped = combinedMesh.Clipped(clippingBounds);

            if (TilingDefaults.PARENT_MESH_VERTEX_MERGE_EPSILON > 0)
            {
                combinedClipped.MergeNearbyVertices(TilingDefaults.PARENT_MESH_VERTEX_MERGE_EPSILON);
            }

            if (combinedClipped.Faces.Count == 0)
            {
                throw new Exception("parent tile mesh empty");
            }

            Vector3? upAxis = MeshSkirt.SkirtAxis(project.SkirtMode);

            // if the combined mesh is already less than the target face count we can skip the ResampleDecimated()
            // also has the benifit of avoiding ResampleDecimated() on low face count meshes which can sometimes fail
            var parentMesh = combinedClipped;
            if (parentMesh.Faces.Count > project.MaxFacesPerTile)
            {
                info($"decimating parent tile from {Fmt.KMG(parentMesh.Faces.Count)} " +
                     $"to {Fmt.KMG(project.MaxFacesPerTile)} tris");

                var srcBounds =
                    BoundingBoxExtensions.CreateScaled(clippingBounds, TilingDefaults.PARENT_DECIMATE_BOUNDS_RATIO);
                var decimateSrc = combinedMesh.Clipped(srcBounds);

                if (!decimateSrc.HasNormals)
                {
                    decimateSrc.GenerateVertexNormals();
                }

                int targetTris = (int)(project.MaxFacesPerTile * TilingDefaults.PARENT_FACE_COUNT_RATIO);
                double samplesPerFace = TilingDefaults.PARENT_SAMPLES_PER_FACE;
                parentMesh = decimateSrc.ResampleDecimated(targetTris, project.ParentReconstructionMethod,
                                                           clippingBounds, upAxis, samplesPerFace);
                //note: ResampleDecimated() calls Mesh.Clean() and preserves normals
            }
            else
            {
                info($"not decimating parent tile, {Fmt.KMG(parentMesh.Faces.Count)} < " +
                     $"{Fmt.KMG(project.MaxFacesPerTile)} tris");
                if (!parentMesh.HasNormals)
                {
                    parentMesh.GenerateVertexNormals();
                }
            } 

            parentBounds = BoundingBoxExtensions.Union(parentBounds, parentMesh.Bounds());
            node.GetComponent<NodeBounds>().Bounds = parentBounds;

            int textureSize = 0;
            bool orbitalTile = false;
            string tileType = "";
            if (project.TextureMode != TextureMode.None)
            {
                orbitalTile = project.IsOrbitalTile(parentBounds);
                tileType = orbitalTile ? "orbital " : "";
                double texelsPerMeter = orbitalTile ? project.MaxOrbitalTexelsPerMeter : project.MaxTexelsPerMeter;
                textureSize = GetTileResolution(parentMesh, project.MaxTextureResolution, -1, texelsPerMeter,
                                                project.PowerOfTwoTextures, info);
            }

            Image parentImg = null, parentIndex = null;
            if (project.TextureMode != TextureMode.None && project.AtlasMode != AtlasMode.None && textureSize > 0)
            {
                var logger = new ThunkLogger() { Info = info, Warn = warn, Error = error };

                TextureProjector textureProjector = null;
                Image textureImage = null;
                if (project.TextureProjectorGuid != Guid.Empty &&
                    (project.TextureMode == TextureMode.Clip || project.AtlasMode == AtlasMode.Project))
                {
                    textureProjector =
                        pipeline.GetDataProduct<TextureProjector>(project, project.TextureProjectorGuid, noCache: true);
                    var texGuid = textureProjector.TextureGuid;
                    if (project.TextureMode == TextureMode.Clip && texGuid != Guid.Empty)
                    {
                        textureImage = pipeline.GetDataProduct<PngDataProduct>(project, texGuid, noCache: true).Image;
                    }
                }

                switch (textureProjector != null ? AtlasMode.Project :
                        orbitalTile ? AtlasMode.Heightmap : project.AtlasMode)
                {
                    case AtlasMode.Project:
                    {
                        if (textureProjector == null)
                        {
                            throw new Exception("no texture projector to atlas parent tile with texture projection");
                        }
                        info($"atlassing {tileType}parent tile with texture projection");
                        parentMesh.ProjectTexture(textureProjector.ImageWidth, textureProjector.ImageHeight,
                                                  textureProjector.CameraModel, textureProjector.MeshToImage);
                        numProjectAtlas++;
                        break;
                    }
                    case AtlasMode.UVAtlas:
                    {
                        info($"atlassing {tileType}parent tile with UVAtlas, resolution {textureSize}, " +
                             $"max stretch {project.MaxTextureStretch}");
                        if (!UVAtlas.Atlas(parentMesh, textureSize, textureSize, maxStretch: project.MaxTextureStretch,
                                           logger: logger, fallbackToNaive: false, maxSec: project.MaxUVAtlasSec))
                        {
                            warn($"failed to atlas {tileType}parent tile with UVAtlas, falling back to heightmap");
                            parentMesh.HeightmapAtlas(upAxis ?? Vector3.UnitZ, swapUV: true);
                            numHeightmapAtlas++;
                        }
                        else
                        {
                            numUVatlas++;
                        }
                        break;
                    }
                    case AtlasMode.Heightmap:
                    {
                        //swap U and V because mission surface frames are typically X north, Y east
                        //this doesn't really matter here except that texture images created to match these flipped UVs
                        //will match the orientation of other debug images
                        info($"atlassing {tileType}parent tile with heightmap atlas");
                        parentMesh.HeightmapAtlas(upAxis ?? Vector3.UnitZ, swapUV: true);
                        numHeightmapAtlas++;
                        break;
                    }
                    case AtlasMode.Naive:
                    {
                        info($"atlassing {tileType}parent tile with naive atlas");
                        parentMesh.NaiveAtlas();
                        numNaiveAtlas++;
                        break;
                    }
                    case AtlasMode.Manifold:
                    {
                        info($"atlassing {tileType}parent tile with manifold atlas");
                        if (!parentMesh.ManifoldAtlas())
                        {
                            //this is expected for a non-convex mesh, info not warn
                            info("failed to manifold atlas parent tile, falling back to UVAtlas");
                            if (!UVAtlas.Atlas(parentMesh, textureSize, textureSize,
                                               maxStretch: project.MaxTextureStretch, logger: logger,
                                               fallbackToNaive: false, maxSec: project.MaxUVAtlasSec))
                            {
                                warn($"failed to atlas {tileType}parent tile with UVAtlas, falling back to heightmap");
                                parentMesh.HeightmapAtlas(upAxis ?? Vector3.UnitZ, swapUV: true);
                                numHeightmapAtlas++;
                            }
                            else
                            {
                                numUVatlas++;
                            }
                        }
                        else
                        {
                            numManifoldAtlas++;
                        }
                        break;
                    }
                    default: throw new Exception("unsupported atlas mode for parent tile " + project.AtlasMode);
                }

                if (project.TextureMode == TextureMode.Clip && textureProjector != null && textureImage != null)
                {
                    if (depMeshImagePairs.All(mip => mip.Index != null))
                    {
                        parentIndex = new Image(3, textureImage.Width, textureImage.Height);
                        for (int r = 0; r < parentIndex.Height; r++)
                        {
                            for (int c = 0; c < parentIndex.Width; c++)
                            {
                                parentIndex[0, r, c] = Observation.MIN_INDEX;
                                parentIndex[1, r, c] = r;
                                parentIndex[2, r, c] = c;
                            }
                        }
                    }
                    info($"clipping {textureSize}x{textureSize} parent tile texture");
                    var tmc = new TexturedMeshClipper(powerOfTwoTextures: project.PowerOfTwoTextures, logger: logger);
                    var pair = tmc.RemapMeshClipImage(parentMesh, textureImage, parentIndex, textureSize);
                    parentMesh = pair.Mesh;
                    parentImg = pair.Image;
                    parentIndex = pair.Index;
                }
                else
                {
                    parentMesh.RescaleUVsForTexture(textureSize, textureSize, project.MaxTextureStretch);
                    //we need to bake parent tile textures even when textureMode is Clip
                    //unless we also have a texture projector to assign appropriate UVs
                    info($"baking {textureSize}x{textureSize} parent tile texture");
                    var tb = new TextureBaker(depMeshImagePairs);
                    parentImg = tb.Bake(parentMesh, textureSize, textureSize, out parentIndex);
                    //note that if textureMode is clip then leaf tile textures may have actually been clipped
                    //even though we are baking here
                    //because leave tiles can take their UVs from the input meshes
                    //but a parent tile can only get UVs usable for clipping by texture projection
                }

                if (project.MaxTextureStretch < 1 && !project.PowerOfTwoTextures)
                {
                    parentImg = parentMesh.ClipImageAndRemapUVs(parentImg, ref parentIndex);
                }
            }

            node.AddComponent(new MeshImagePair(parentMesh, parentImg, parentIndex));

            //prevent UpdateGeometricError() from using child meshes if parentMesh == combinedClipped
            //meaning we chose not to decimate the merged clipped child meshes
            //in that case the geometric error is just the max of the dependencies' errors
            //empty (not null) if parentMesh == combinedClipped
            depMeshes = parentMesh != combinedClipped ? new Mesh[] { combinedClipped } : new Mesh[] {};

            info("computing parent tile geometric error");
            node.UpdateGeometricError(dependencies, depMeshes.ToList(), info);
        }

        /// <summary>
        /// Given a list of nodes, connect them in a tree based on name prefix convention and return the root
        ///
        /// each node name is of the form ABCDE... where
        /// A is the index of a child of the root
        /// B is the index of a child of the node corresponding to A, etc
        /// thus each node name encodes a full path from the root to the node
        /// and the collection of all leaf names encodes the full tree topology
        ///
        /// as long as all the leaves are provided this function will reconstitute any missing parent nodes
        /// </summary>
        /// <param name="nodes"></param>
        /// <returns></returns>
        public static SceneNode ConnectNodesByName(IEnumerable<SceneNode> nodes)
        {
            Dictionary<string, SceneNode> lookup = new Dictionary<string, SceneNode>();
            foreach(var node in nodes)
            {
                lookup.Add(node.Name, node);
            }
            Queue<SceneNode> nodesToConnect = new Queue<SceneNode>(nodes);
            SceneNode root = null;
            while(nodesToConnect.Count != 0)
            {
                var node = nodesToConnect.Dequeue();
                if(node.Name == "root")
                {
                    root = node;
                    continue;
                }
                string parentId = (node.Name.Length == 1) ? "root" : node.Name.Substring(0, node.Name.Length - 1);
                if(!lookup.ContainsKey(parentId))
                {
                    var p = new SceneNode(parentId);
                    nodesToConnect.Enqueue(p);
                    lookup.Add(parentId, p);
                }
                var parent = lookup[parentId];
                node.Transform.SetParent(parent.Transform);
            }
            return root;
        }
        
        /// <summary>
        /// Given a tree with leaves that have meshes, compute bounding boxes up the tree such that
        /// parents bounding boxes fully enclose their children.  Add NodeBounds components onto the
        /// nodes of the tree and set their bounds accordingly.  If parent nodes have mesh data their
        /// meshes will also be enclosed by the calculated bounds.
        /// </summary>
        /// <param name="root"></param>
        public static void ComputeBounds(SceneNode root, bool useExistingLeafBounds = false)
        {
            HashSet<SceneNode> curParents = new HashSet<SceneNode>();
            foreach (var leaf in root.Leaves())
            {
                if (!useExistingLeafBounds || !leaf.HasComponent<NodeBounds>())
                {
                    var pair = leaf.GetComponent<MeshImagePair>();
                    var meshBounds = pair.Mesh.Bounds();
                    if (meshBounds.IsEmpty())
                    {
                        meshBounds = new BoundingBox();
                    }
                    leaf.GetOrAddComponent<NodeBounds>().Bounds = meshBounds;
                }
                if (leaf.Parent != null)
                {
                    curParents.Add(leaf.Parent);
                }
            }
            while (curParents.Count > 0)
            {
                HashSet<SceneNode> nextParents = new HashSet<SceneNode>();
                foreach (var p in curParents)
                {
                    p.GetOrAddComponent<NodeBounds>().Bounds =
                        BoundingBoxExtensions.Union(p.Children.Select(c => c.GetOrAddComponent<NodeBounds>().Bounds)
                                                    .ToArray());
                    if (p.HasComponent<MeshImagePair>() && p.GetComponent<MeshImagePair>().Mesh != null)
                    {
                        p.GetComponent<NodeBounds>().Bounds =
                            BoundingBoxExtensions.Union(p.GetComponent<MeshImagePair>().Mesh.Bounds(),
                                                        p.GetComponent<NodeBounds>().Bounds);
                    }
                    if (p.Parent != null)
                    {
                        nextParents.Add(p.Parent);
                    }
                }
                curParents = nextParents;
            }           
        }

        /// <summary>
        /// Add or recompute NodeGeometricError.
        ///
        /// Assumes the node's dependencies are available and already have their errors computed.
        ///
        /// https://github.com/CesiumGS/3d-tiles/blob/master/3d-tiles-overview.pdf
        /// discusses specifically what the geometric error is supposed to represent in section 5 - Geometric Error:
        /// > Each tileset and each tile has a geometricError property that quantifies the error
        /// > of the simplified geometry compared to the actual geometry.
        ///
        /// For a leaf node the error is always 0.
        ///
        /// For a node with no mesh of its own the error is the max of its dependencies' errors.
        ///
        /// Otherwise, for a parent node we essentially compute the the Hausdorff distance between the decimated mesh,
        /// if any, vs the dependency meshes.  We then add that to the maximum geometric error of any of the
        /// dependencies.  Because none of that will account for situations where the parent geometry is good but its
        /// texture is less good, we also estimate the effective parent mesh texture resolution in units of lineal
        /// meters per texel, multiplied by an adjustment factor.  If that is larger than th Hausdorff distance it is
        /// used instead (i.e. instead of the sum of the Hausdorff distance and the max dependency error).
        ///
        /// Yes, this is quite confusing.  Consider the effect in the viewer, where the maximum screenspace error
        /// threshold is set at say 16 pixels.
        ///
        /// First consider the nominal  case when the tile geometric error dominates the  texture error.  The tile error
        /// will be transformed  from meters to screen pixels depending  on the current distance from the  camera to the
        /// tile.   This computation  is done  assuming the  tile error  is measured  in lineal  meters.  If  the actual
        /// geometric error, say 0.05m, dominates the tile texture error and the effective conversion factor from linear
        /// error in meters to  screen pixels (dependent on the camera FOV, screen  resolution, and distance from camera
        /// to terrain) is greater than 320 then it will call for switching to the next finer LOD, because errors in the
        /// currently displayed geometry can move things more than 0.05m  * 320px/m = 16 px from  where they should be.
        ///   
        /// Now consider the case where the tile texture error dominates, say 0.05, meaning the actual tile texture
        /// resolution is 0.0125 lineal meters per texel if TilingDefaults.TEXTURE_ERROR_MULTIPLIER=4.  Then one lineal
        /// texel maps to 0.0125*320 = 4 lineal pixels (16 square pixels) on screen, a relatively large amount of
        /// texture magnification.  The next finer LOD will be triggered because of the texture magnification.
        /// </summary>
        public static double UpdateGeometricError(this SceneNode node,
                                                  List<SceneNode> dependencies,
                                                  List<Mesh> dependencyMeshes = null,
                                                  Action<string> info = null)
        {
            info = info ?? (msg => {});
            int nd = dependencies.Count;

            if (node.IsLeaf)
            {
                node.GetOrAddComponent<NodeGeometricError>().Error = 0;
                info($"{node.Name} is a leaf, geometric error 0");
                return 0;
            }

            double maxDepError = 0;
            foreach (var dep in dependencies)
            {
                var depError = dep.GetComponent<NodeGeometricError>();
                if (depError != null)
                {
                    maxDepError = Math.Max(depError.Error, maxDepError);
                }
            }

            var mip = node.GetComponent<MeshImagePair>();
            if (mip == null || mip.Mesh == null || !mip.Mesh.HasFaces)
            {
                node.GetOrAddComponent<NodeGeometricError>().Error = maxDepError;
                info($"{node.Name} empty, geometric error {maxDepError:F3} (max of {nd} dependencies)");
                return maxDepError;
            }

            if (dependencyMeshes == null)
            {
                dependencyMeshes = dependencies
                    .Select(d => d.GetComponent<MeshImagePair>())
                    .Where(p => p != null && p.Mesh != null && p.Mesh.HasFaces)
                    .Select(p => p.Mesh)
                    .ToList();
            }
            else
            {
                dependencyMeshes = dependencyMeshes.Where(m => m != null && m.HasFaces).ToList();
            }

            double meshError = 0; //meters
            if (dependencyMeshes.Count > 0)
            {
                double accuracy = 0.001; //1mm
                var bounds = node.GetComponent<NodeBounds>();
                if (bounds != null)
                {
                    accuracy = bounds.Bounds.MaxDimension() * TilingDefaults.PARENT_HAUSDORFF_RELATIVE_ACCURACY;
                }
                //the merged dependency meshes can be a significant superset of this node's mesh
                //just compute the unidirectional Hausdorff distance from this node's mesh to the merged dep meshes
                bool symmetric = false;
                meshError = maxDepError + mip.Mesh.HausdorffDistance(accuracy, symmetric, dependencyMeshes.ToArray());
            }

            info($"{node.Name} mesh error {meshError:F3} (incl max {maxDepError:F3} of {nd} dependencies)");

            double textureError = 0; //lineal meters per texel
            if (useTextureError && mip.Image != null)
            {
                double mult = TilingDefaults.TEXTURE_ERROR_MULTIPLIER;
                double pixelArea = mip.Mesh.ComputePixelArea(mip.Image);
                double surfaceArea = -1;
                if (pixelArea > 0)
                {
                    surfaceArea = mip.Mesh.SurfaceArea();
                    textureError = mult * Math.Sqrt(surfaceArea / pixelArea);
                }
                info($"{node.Name} texture error {textureError:F3}" +
                     (pixelArea > 0 ? $" = {mult:F3} * sqrt({surfaceArea:F3}m^2 / {pixelArea:F3}px^2)" : ""));
            }

            double error = Math.Max(meshError, textureError);
            info($"{node.Name} geometric error {error:F3}, meshError={meshError:F3}, textureError={textureError:F3}");

            node.GetOrAddComponent<NodeGeometricError>().Error = error;
            return error;
        }

        public static void DumpStats(this SceneNode root, Action<string> writeLine)
        {
            var nodes = root.DepthFirstTraverse().ToList();

            foreach (var node in nodes)
            {
                if (node.HasComponent<MeshImagePair>() && !node.HasComponent<MeshImagePairStats>())
                {
                    node.AddComponent(new MeshImagePairStats(node.GetComponent<MeshImagePair>()));
                }
            }

            void dumpTextureStats(IEnumerable<MeshImagePairStats> mipStats, string prefix = "")
            {
                var minUVArea = mipStats.Min(s => s.UVArea);
                var maxUVArea = mipStats.Max(s => s.UVArea);
                
                var texRes = mipStats
                    .Where(s => s.MeshArea > 0 && s.UVArea > 0 && s.NumPixels > 0)
                    .Select(s => (s.UVArea * s.NumPixels) / (s.MeshArea * 100 * 100))
                    .OrderBy(v => v);
                var minTexRes = texRes.FirstOrDefault();
                var maxTexRes = texRes.LastOrDefault();

                if (minTexRes > 0 || maxTexRes > 0)
                {
                    writeLine(string.Format("{0}texture utilization {1:f3}-{2:f3}; texels/cm^2 {3:f3}-{4:f3}",
                                            prefix, minUVArea, maxUVArea, minTexRes, maxTexRes));
                }
            }

            void dumpLevel(IEnumerable<SceneNode> level, string msg)
            {
                var errors = level
                    .Where(node => node.HasComponent<NodeGeometricError>())
                    .Select(node => node.GetComponent<NodeGeometricError>().Error)
                    .OrderBy(e => e)
                    .ToList();
                if (errors.Count > 0)
                {
                    msg += string.Format("; geometric error {0:f3}-{1:f3}", errors.First(), errors.Last());
                }

                writeLine(msg);

                var bounds = level
                    .Where(node => node.HasComponent<NodeBounds>())
                    .Select(node => node.GetComponent<NodeBounds>().Bounds)
                    .OrderBy(b => b.Volume())
                    .ToList();
                if (bounds.Count > 0)
                {
                    var minBounds = bounds.First();
                    var maxBounds = bounds.Last();
                    msg = string.Format("  {0} bounds {1}{2}",
                                        bounds.Count, minBounds.FmtExtent(),
                                        bounds.Count > 1 ? ("-" + maxBounds.FmtExtent()) : "");
                    writeLine(msg);
                }

                var mipStats = level
                    .Where(node => node.HasComponent<MeshImagePairStats>())
                    .Select(node => node.GetComponent<MeshImagePairStats>())
                    .ToList();

                if (mipStats.Count > 0)
                {
                    msg = "";

                    var imgStats = mipStats.Where(s => s.NumPixels > 0).OrderBy(s => s.NumPixels).ToList();
                    if (imgStats.Count > 0)
                    {
                        var minImg = imgStats.First();
                        var maxImg = imgStats.Last();
                        msg = string.Format("  {0} images {1}x{2}-{3}x{4}, {5} total pixels", imgStats.Count,
                                            minImg.ImageWidth, minImg.ImageHeight,
                                            maxImg.ImageWidth, maxImg.ImageHeight,
                                            Fmt.KMG(imgStats.Sum(s => (long)(s.NumPixels))));
                    }

                    int numIndices = mipStats.Count(s => s.HasIndex);
                    if (numIndices > 0)
                    {
                        msg += string.Format("{0}{1} indices", (msg != "") ? ", " : "", numIndices);
                    }

                    var vertStats = mipStats.Where(s => s.NumVerts > 0).OrderBy(s => s.NumVerts).ToList();
                    if (vertStats.Count > 0)
                    {
                        msg += (msg != "") ? ", " : "  "; 

                        var minVerts = vertStats.First().NumVerts;
                        var maxVerts = vertStats.Last().NumVerts;
                        msg += string.Format("{0} meshes {1}-{2} verts", vertStats.Count,
                                             Fmt.KMG(minVerts), Fmt.KMG(maxVerts));

                        var triStats = mipStats.Where(s => s.NumTris > 0).OrderBy(s => s.NumTris).ToList();
                        if (triStats.Count > 0)
                        {
                            var minTris = triStats.First().NumTris;
                            var maxTris = triStats.Last().NumTris;

                            var minMeshArea = triStats.Min(s => s.MeshArea);
                            var maxMeshArea = triStats.Max(s => s.MeshArea);
                            
                            var minTriArea = triStats.Min(s => s.MinTriArea);
                            var maxTriArea = triStats.Max(s => s.MaxTriArea);
                            string triAreaUnit = "m^2";
                            if (minTriArea < 0.001)
                            {
                                minTriArea *= 1e6;
                                maxTriArea *= 1e6;
                                triAreaUnit = "mm^2";
                            }

                            msg += string.Format(", {0}-{1} tris ({2} total), mesh area {3:f3}-{4:f3}m^2 ({5} total)"
                                                 + "; tri area {6:f3}-{7:f3}{8}",
                                                 Fmt.KMG(minTris), Fmt.KMG(maxTris),
                                                 Fmt.KMG(triStats.Sum(s => (long)(s.NumTris))),
                                                 minMeshArea, maxMeshArea, Fmt.KMG(triStats.Sum(s => s.MeshArea)),
                                                 minTriArea, maxTriArea, triAreaUnit);
                            writeLine(msg);

                            dumpTextureStats(triStats, "  ");
                        }
                    }
                }
                else
                {
                    var faceCounts = level
                        .Where(node => node.HasComponent<FaceCount>())
                        .Select(node => node.GetComponent<FaceCount>())
                        .ToList();
                    if (faceCounts.Count > 0)
                    {
                        int minTris = faceCounts.Select(fc => fc.NumTris).Min();
                        int maxTris = faceCounts.Select(fc => fc.NumTris).Max();
                        writeLine($"  {Fmt.KMG(minTris)}-{Fmt.KMG(maxTris)} tris");
                    }
                }
            }

            var levels = nodes.GroupBy(n => n.Transform.Depth()).OrderBy(g => g.Key);

            foreach (var level in levels)
            {
                string msg = string.Format("level {0}: {1} tiles, {2} leaves",
                                           level.Key, level.Count(), level.Count(n => n.IsLeaf));

                var parents = level.Where(node => node.Children.Count() > 0).ToList();
                if (parents.Count > 0)
                {
                    int minBranch = parents.Min(node => node.Children.Count());
                    if (minBranch > 0)
                    {
                        msg += string.Format("; branching factor {0}", minBranch);
                        int maxBranch = parents.Max(node => node.Children.Count());
                        if (maxBranch > minBranch)
                        {
                            msg += string.Format("-{0}", maxBranch);
                        }
                    }
                }

                dumpLevel(level, msg);
            }

            var leaves = nodes.Where(node => node.IsLeaf);
            var leafLevels = leaves.Select(n => n.Transform.Depth()).DefaultIfEmpty(-1);
            dumpLevel(leaves, string.Format("{0} leaves at level(s) {1}-{2}",
                                            leaves.Count(), leafLevels.Min(), leafLevels.Max()));

            writeLine(string.Format("tile tree has {0} levels, {1} total tiles, {2} leaves",
                                    levels.Count(), nodes.Count, nodes.Count(node => node.IsLeaf)));

            var meshStats = nodes
                .Where(node => node.HasComponent<MeshImagePairStats>())
                .Select(node => node.GetComponent<MeshImagePairStats>())
                .Where(s => s.NumTris > 0)
                .ToList();

            writeLine(string.Format("{0} meshes, {1} textures, {2} triangles, {3} texels",
                                    meshStats.Count, meshStats.Count(s => s.NumPixels > 0),
                                    Fmt.KMG(meshStats.Sum(s => (long)(s.NumTris))),
                                    Fmt.KMG(meshStats.Sum(s => (long)(s.NumPixels)))));

            if (meshStats.Count > 0)
            {
                dumpTextureStats(meshStats);
            }
            else
            {
                var faceCounts = nodes
                    .Where(node => node.HasComponent<FaceCount>())
                    .Select(node => node.GetComponent<FaceCount>())
                    .ToList();
                if (faceCounts.Count > 0)
                {
                    int minTris = faceCounts.Select(fc => fc.NumTris).Min();
                    int maxTris = faceCounts.Select(fc => fc.NumTris).Max();
                    writeLine($"{faceCounts.Count} meshes, {Fmt.KMG(minTris)}-{Fmt.KMG(maxTris)} triangles");
                }
            }
        }
    }
}
