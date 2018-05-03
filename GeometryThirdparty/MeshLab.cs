using log4net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Util;
using System.IO;
using Microsoft.Xna.Framework;
using OPS.MathExtensions;
using System.Text.RegularExpressions;

namespace OPS.Geometry
{
    /// <summary>
    /// Exposes mesh operations that are implemented in MeshLab
    /// Filters are tested against MeshLab_2016
    /// </summary>
    public class MeshLab
    {
        private static readonly ILog logger = LogManager.GetLogger(typeof(MeshLab));

        /// <summary>
        /// Expose ability to override the meshlab server directory to outside packages that don't have access to MeshLabRunner directly
        /// Set this to the directory containing your meshlab executable
        /// </summary>
        public static string MeshLabServerDirectory
        {
            get { return MeshLabRunner.MeshlabServerDirectory; }
            set { MeshLabRunner.MeshlabServerDirectory = value; }
        }

        /// <summary>
        /// Return a copy of the input mesh with computed vertex normals
        /// Works on both meshes with faces and point clouds (without uvs)
        /// Retains uvs and colors when working with meshes that have faces
        /// Retains colors but looses uvs when working with point clouds
        /// </summary>
        /// <param name="m"></param>
        /// <returns></returns>
        public static Mesh ComputeNormals(Mesh m)
        {
            string script = @"<!DOCTYPE FilterScript>
<FilterScript>
 <filter name=""Re-Compute Vertex Normals"">
  <Param enum_val1=""By Angle"" enum_val3=""As defined by N. Max"" value=""0"" description=""Weighting Mode:"" type=""RichEnum"" enum_cardinality=""4"" enum_val2=""By Area"" name=""weightMode"" enum_val0=""None (avg)"" tooltip=""""/>
 </filter>
</FilterScript>";
            if (!m.HasFaces)
            {
                script = @"<!DOCTYPE FilterScript>
<FilterScript>
 <filter name=""Compute normals for point sets"">
  <Param name=""K"" type=""RichInt"" description=""Neighbour num"" tooltip=""The number of neighbors used to estimate normals."" value=""10""/>
  <Param name=""smoothIter"" type=""RichInt"" description=""Smooth Iteration"" tooltip=""The number of smoothing iteration done on the p used to estimate and propagate normals."" value=""0""/>
  <Param name=""flipFlag"" type=""RichBool"" description=""Flip normals w.r.t. viewpoint"" tooltip=""If the 'viewpoint' (i.e. scanner position) is known, it can be used to disambiguate normals orientation, so that all the normals will be oriented in the same direction."" value=""false""/>
  <Param name=""viewPos"" type=""RichPoint3f"" description=""Viewpoint Pos."" tooltip=""The viewpoint position can be set by hand (i.e. getting the current viewpoint) or it can be retrieved from mesh camera, if the viewpoint position is stored there."" x=""0"" y=""0"" z=""0""/>
 </filter>
</FilterScript>";
            }
            MeshLabOutputAttributes attributes = new MeshLabOutputAttributes(normals: true, uvs: m.HasUVs, colors: m.HasColors);
            MeshLabRunner mlr = new MeshLabRunner(m, script, attributes, ".obj", ".obj");
            Mesh result = mlr.Run();
            return result;
        }

        /// <summary>
        /// Sample the input mesh using poission-disk sampling and return a point cloud of points on the mesh
        /// Retains normals but not uvs or colors
        /// Works on meshes with faces but NOT point sets
        /// </summary>
        /// <param name="m"></param>
        /// <param name="approximateNumberSamples"></param>
        /// <returns></returns>
        public static Mesh Sample(Mesh m, int approximateNumberSamples = 2000)
        {
            if (m.Faces.Count == 0)
            {
                throw new MeshLabException("Cannot sample mesh without faces");
            }
            string script = @"<!DOCTYPE FilterScript>
<FilterScript>
 <filter name=""Poisson-disk Sampling"">
  <Param value=""{0}"" description=""Number of samples"" type=""RichInt"" name=""SampleNum"" tooltip=""The desired number of samples. The ray of the disk is calculated according to the sampling density.""/>
  <Param min=""0"" value=""0"" description=""Explicit Radius"" type=""RichAbsPerc"" max=""0.302604"" name=""Radius"" tooltip=""If not zero this parameter override the previous parameter to allow exact radius specification""/>
  <Param value=""20"" description=""MonterCarlo OverSampling"" type=""RichInt"" name=""MontecarloRate"" tooltip=""The over-sampling rate that is used to generate the intial Montecarlo samples (e.g. if this parameter is &lt;i>K&lt;/i> means that&lt;i>K&lt;/i> x &lt;i>poisson sample&lt;/i> points will be used). The generated Poisson-disk samples are a subset of these initial Montecarlo samples. Larger this number slows the process but make it a bit more accurate.""/>
  <Param value=""false"" description=""Save Montecarlo"" type=""RichBool"" name=""SaveMontecarlo"" tooltip=""If true, it will generate an additional Layer with the montecarlo sampling that was pruned to build the poisson distribution.""/>
  <Param value=""false"" description=""Approximate Geodesic Distance"" type=""RichBool"" name=""ApproximateGeodesicDistance"" tooltip=""If true Poisson Disc distances are computed using an approximate geodesic distance, e.g. an euclidean distance weighted by a function of the difference between the normals of the two points.""/>
  <Param value=""false"" description=""Base Mesh Subsampling"" type=""RichBool"" name=""Subsample"" tooltip=""If true the original vertices of the base mesh are used as base set of points. In this case the SampleNum should be obviously much smaller than the original vertex number.&lt;br>Note that this option is very useful in the case you want to subsample a dense point cloud.""/>
  <Param value=""false"" description=""Refine Existing Samples"" type=""RichBool"" name=""RefineFlag"" tooltip=""If true the vertices of the below mesh are used as starting vertices, and they will utterly refined by adding more and more points until possible. ""/>
  <Param value=""0"" description=""Samples to be refined"" type=""RichMesh"" name=""RefineMesh"" tooltip=""Used only if the above option is checked. ""/>
  <Param value=""true"" description=""Best Sample Heuristic"" type=""RichBool"" name=""BestSampleFlag"" tooltip=""If true it will use a simple heuristic for choosing the samples. At a small cost (it can slow a bit the process) it usually improve the maximality of the generated sampling. ""/>
  <Param value=""10"" description=""Best Sample Pool Size"" type=""RichInt"" name=""BestSamplePool"" tooltip=""Used only if the Best Sample Flag is true. It control the number of attempt that it makes to get the best sample. It is reasonable that it is smaller than the Montecarlo oversampling factor.""/>
  <Param value=""false"" description=""Exact number of samples"" type=""RichBool"" name=""ExactNumFlag"" tooltip=""If requested it will try to do a dicotomic search for the best poisson disk radius that will generate the requested number of samples with a tolerance of the 0.5%. Obviously it takes much longer.""/>
  <Param value=""1"" description=""Radius Variance"" type=""RichFloat"" name=""RadiusVariance"" tooltip=""The radius of the disk is allowed to vary between r and r*var. If this parameter is 1 the sampling is the same of the Poisson Disk Sampling""/>
 </filter> 
 <filter name=""Delete Current Mesh""/>
</FilterScript>
";
            string meshlabScript = string.Format(script, approximateNumberSamples);
            MeshLabOutputAttributes attributes = new MeshLabOutputAttributes(normals: m.HasNormals, uvs: false, colors: false);
            MeshLabRunner mlr = new MeshLabRunner(m, meshlabScript, attributes, ".obj", ".obj");
            Mesh result = mlr.Run();
            return result;
        }

        /// <summary>
        /// Decimate the input mesh to the target number of faces using quadratic edge collapse 
        /// Does not retain uvs or colors but will recompute normals if they exist in the input mesh
        /// Works on meshes with faces but NOT point sets
        /// </summary>
        /// <param name="m"></param>
        /// <param name="targetFaces"></param>
        /// <returns></returns>
        public static Mesh Decimate(Mesh m, int targetFaces = 2000)
        {
            if (m.Faces.Count == 0)
            {
                throw new MeshLabException("Cannot decimate mesh without faces");
            }
            string script = @"<!DOCTYPE FilterScript>
<FilterScript>
 <filter name=""Simplification: Quadric Edge Collapse Decimation"">
  <Param value=""{0}"" description=""Target number of faces"" type=""RichInt"" name=""TargetFaceNum"" tooltip=""The desired final number of faces.""/>
  <Param value=""0"" description=""Percentage reduction (0..1)"" type=""RichFloat"" name=""TargetPerc"" tooltip=""If non zero, this parameter specifies the desired final size of the mesh as a percentage of the initial size.""/>
  <Param value=""0.3"" description=""Quality threshold"" type=""RichFloat"" name=""QualityThr"" tooltip=""Quality threshold for penalizing bad shaped faces.&lt;br>The value is in the range [0..1]&#xa; 0 accept any kind of face (no penalties),&#xa; 0.5  penalize faces with quality &lt; 0.5, proportionally to their shape&#xa;""/>
  <Param value=""false"" description=""Preserve Boundary of the mesh"" type=""RichBool"" name=""PreserveBoundary"" tooltip=""The simplification process tries to do not affect mesh boundaries during simplification""/>
  <Param value=""1"" description=""Boundary Preserving Weight"" type=""RichFloat"" name=""BoundaryWeight"" tooltip=""The importance of the boundary during simplification. Default (1.0) means that the boundary has the same importance of the rest. Values greater than 1.0 raise boundary importance and has the effect of removing less vertices on the border. Admitted range of values (0,+inf). ""/>
  <Param value=""false"" description=""Preserve Normal"" type=""RichBool"" name=""PreserveNormal"" tooltip=""Try to avoid face flipping effects and try to preserve the original orientation of the surface""/>
  <Param value=""false"" description=""Preserve Topology"" type=""RichBool"" name=""PreserveTopology"" tooltip=""Avoid all the collapses that should cause a topology change in the mesh (like closing holes, squeezing handles, etc). If checked the genus of the mesh should stay unchanged.""/>
  <Param value=""true"" description=""Optimal position of simplified vertices"" type=""RichBool"" name=""OptimalPlacement"" tooltip=""Each collapsed vertex is placed in the position minimizing the quadric error.&#xa; It can fail (creating bad spikes) in case of very flat areas. &#xa;If disabled edges are collapsed onto one of the two original vertices and the final mesh is composed by a subset of the original vertices. ""/>
  <Param value=""false"" description=""Planar Simplification"" type=""RichBool"" name=""PlanarQuadric"" tooltip=""Add additional simplification constraints that improves the quality of the simplification of the planar portion of the mesh.""/>
  <Param value=""false"" description=""Weighted Simplification"" type=""RichBool"" name=""QualityWeight"" tooltip=""Use the Per-Vertex quality as a weighting factor for the simplification. The weight is used as a error amplification value, so a vertex with a high quality value will not be simplified and a portion of the mesh with low quality values will be aggressively simplified.""/>
  <Param value=""true"" description=""Post-simplification cleaning"" type=""RichBool"" name=""AutoClean"" tooltip=""After the simplification an additional set of steps is performed to clean the mesh (unreferenced vertices, bad faces, etc)""/>
  <Param value=""false"" description=""Simplify only selected faces"" type=""RichBool"" name=""Selected"" tooltip=""The simplification is applied only to the selected set of faces.&#xa; Take care of the target number of faces!""/>
 </filter>
 <filter name=""Re-Compute Vertex Normals"">
  <Param enum_val1=""By Angle"" enum_val3=""As defined by N. Max"" value=""0"" description=""Weighting Mode:"" type=""RichEnum"" enum_cardinality=""4"" enum_val2=""By Area"" name=""weightMode"" enum_val0=""None (avg)"" tooltip=""""/>
 </filter>
</FilterScript>
";
            string meshlabScript = string.Format(script, targetFaces);
            MeshLabOutputAttributes attributes = new MeshLabOutputAttributes(normals: m.HasNormals, uvs: false, colors: false);
            MeshLabRunner mlr = new MeshLabRunner(m, meshlabScript, attributes, ".obj", ".obj");
            Mesh result = mlr.Run();
            return result;
        }


        /// <summary>
        /// Decimate a mesh to the desired number of faces by sampling it's surface and remeshing the sampled point cloud
        /// Does not retain uvs or colors but will recompute normals if they exist in the input mesh
        /// Works on meshes with faces but NOT point sets
        /// </summary>
        /// <param name="m"></param>
        /// <param name="numSamples"></param>
        /// <param name="targetFaces"></param>
        /// <returns></returns>
        public static Mesh ResampleDecimation(Mesh m, int numSamples = 2000, int targetFaces = 2000)
        {
            if (m.Faces.Count == 0)
            {
                throw new MeshLabException("Cannot Resample Decimate mesh without faces");
            }
            BoundingBox bounds = m.Bounds();
            double deblobRadius =  MathE.Max(bounds.Size().ToDoubleArray()) * 0.2f;
            string script = @"<!DOCTYPE FilterScript>
<FilterScript>
 <filter name=""Re-Compute Vertex Normals"">
  <Param enum_val1=""By Angle"" enum_val3=""As defined by N. Max"" value=""0"" description=""Weighting Mode:"" type=""RichEnum"" enum_cardinality=""4"" enum_val2=""By Area"" name=""weightMode"" enum_val0=""None (avg)"" tooltip=""""/>
 </filter>
 <filter name=""Poisson-disk Sampling"">
  <Param value=""{0}"" description=""Number of samples"" type=""RichInt"" name=""SampleNum"" tooltip=""The desired number of samples. The ray of the disk is calculated according to the sampling density.""/>
  <Param min=""0"" value=""0"" description=""Explicit Radius"" type=""RichAbsPerc"" max=""0.302604"" name=""Radius"" tooltip=""If not zero this parameter override the previous parameter to allow exact radius specification""/>
  <Param value=""20"" description=""MonterCarlo OverSampling"" type=""RichInt"" name=""MontecarloRate"" tooltip=""The over-sampling rate that is used to generate the intial Montecarlo samples (e.g. if this parameter is &lt;i>K&lt;/i> means that&lt;i>K&lt;/i> x &lt;i>poisson sample&lt;/i> points will be used). The generated Poisson-disk samples are a subset of these initial Montecarlo samples. Larger this number slows the process but make it a bit more accurate.""/>
  <Param value=""false"" description=""Save Montecarlo"" type=""RichBool"" name=""SaveMontecarlo"" tooltip=""If true, it will generate an additional Layer with the montecarlo sampling that was pruned to build the poisson distribution.""/>
  <Param value=""false"" description=""Approximate Geodesic Distance"" type=""RichBool"" name=""ApproximateGeodesicDistance"" tooltip=""If true Poisson Disc distances are computed using an approximate geodesic distance, e.g. an euclidean distance weighted by a function of the difference between the normals of the two points.""/>
  <Param value=""false"" description=""Base Mesh Subsampling"" type=""RichBool"" name=""Subsample"" tooltip=""If true the original vertices of the base mesh are used as base set of points. In this case the SampleNum should be obviously much smaller than the original vertex number.&lt;br>Note that this option is very useful in the case you want to subsample a dense point cloud.""/>
  <Param value=""false"" description=""Refine Existing Samples"" type=""RichBool"" name=""RefineFlag"" tooltip=""If true the vertices of the below mesh are used as starting vertices, and they will utterly refined by adding more and more points until possible. ""/>
  <Param value=""0"" description=""Samples to be refined"" type=""RichMesh"" name=""RefineMesh"" tooltip=""Used only if the above option is checked. ""/>
  <Param value=""true"" description=""Best Sample Heuristic"" type=""RichBool"" name=""BestSampleFlag"" tooltip=""If true it will use a simple heuristic for choosing the samples. At a small cost (it can slow a bit the process) it usually improve the maximality of the generated sampling. ""/>
  <Param value=""10"" description=""Best Sample Pool Size"" type=""RichInt"" name=""BestSamplePool"" tooltip=""Used only if the Best Sample Flag is true. It control the number of attempt that it makes to get the best sample. It is reasonable that it is smaller than the Montecarlo oversampling factor.""/>
  <Param value=""false"" description=""Exact number of samples"" type=""RichBool"" name=""ExactNumFlag"" tooltip=""If requested it will try to do a dicotomic search for the best poisson disk radius that will generate the requested number of samples with a tolerance of the 0.5%. Obviously it takes much longer.""/>
  <Param value=""1"" description=""Radius Variance"" type=""RichFloat"" name=""RadiusVariance"" tooltip=""The radius of the disk is allowed to vary between r and r*var. If this parameter is 1 the sampling is the same of the Poisson Disk Sampling""/>
 </filter>
 <filter name=""Delete Current Mesh""/>
 <xmlfilter name=""Screened Poisson Surface Reconstruction"">
  <xmlparam value=""0"" name=""cgDepth""/>
  <xmlparam value=""false"" name=""confidence""/>
  <xmlparam value=""8"" name=""depth""/>
  <xmlparam value=""5"" name=""fullDepth""/>
  <xmlparam value=""8"" name=""iters""/>
  <xmlparam value=""4"" name=""pointWeight""/>
  <xmlparam value=""false"" name=""preClean""/>
  <xmlparam value=""1.5"" name=""samplesPerNode""/>
  <xmlparam value=""1.1"" name=""scale""/>
  <xmlparam value=""false"" name=""visibleLayer""/>
 </xmlfilter>
 <filter name=""Delete Current Mesh""/>
 <filter name=""Simplification: Quadric Edge Collapse Decimation"">
  <Param value=""{2}"" description=""Target number of faces"" type=""RichInt"" name=""TargetFaceNum"" tooltip=""The desired final number of faces.""/>
  <Param value=""0"" description=""Percentage reduction (0..1)"" type=""RichFloat"" name=""TargetPerc"" tooltip=""If non zero, this parameter specifies the desired final size of the mesh as a percentage of the initial size.""/>
  <Param value=""0.3"" description=""Quality threshold"" type=""RichFloat"" name=""QualityThr"" tooltip=""Quality threshold for penalizing bad shaped faces.&lt;br>The value is in the range [0..1]&#xa; 0 accept any kind of face (no penalties),&#xa; 0.5  penalize faces with quality &lt; 0.5, proportionally to their shape&#xa;""/>
  <Param value=""false"" description=""Preserve Boundary of the mesh"" type=""RichBool"" name=""PreserveBoundary"" tooltip=""The simplification process tries to do not affect mesh boundaries during simplification""/>
  <Param value=""1"" description=""Boundary Preserving Weight"" type=""RichFloat"" name=""BoundaryWeight"" tooltip=""The importance of the boundary during simplification. Default (1.0) means that the boundary has the same importance of the rest. Values greater than 1.0 raise boundary importance and has the effect of removing less vertices on the border. Admitted range of values (0,+inf). ""/>
  <Param value=""false"" description=""Preserve Normal"" type=""RichBool"" name=""PreserveNormal"" tooltip=""Try to avoid face flipping effects and try to preserve the original orientation of the surface""/>
  <Param value=""false"" description=""Preserve Topology"" type=""RichBool"" name=""PreserveTopology"" tooltip=""Avoid all the collapses that should cause a topology change in the mesh (like closing holes, squeezing handles, etc). If checked the genus of the mesh should stay unchanged.""/>
  <Param value=""true"" description=""Optimal position of simplified vertices"" type=""RichBool"" name=""OptimalPlacement"" tooltip=""Each collapsed vertex is placed in the position minimizing the quadric error.&#xa; It can fail (creating bad spikes) in case of very flat areas. &#xa;If disabled edges are collapsed onto one of the two original vertices and the final mesh is composed by a subset of the original vertices. ""/>
  <Param value=""false"" description=""Planar Simplification"" type=""RichBool"" name=""PlanarQuadric"" tooltip=""Add additional simplification constraints that improves the quality of the simplification of the planar portion of the mesh.""/>
  <Param value=""false"" description=""Weighted Simplification"" type=""RichBool"" name=""QualityWeight"" tooltip=""Use the Per-Vertex quality as a weighting factor for the simplification. The weight is used as a error amplification value, so a vertex with a high quality value will not be simplified and a portion of the mesh with low quality values will be aggressively simplified.""/>
  <Param value=""true"" description=""Post-simplification cleaning"" type=""RichBool"" name=""AutoClean"" tooltip=""After the simplification an additional set of steps is performed to clean the mesh (unreferenced vertices, bad faces, etc)""/>
  <Param value=""false"" description=""Simplify only selected faces"" type=""RichBool"" name=""Selected"" tooltip=""The simplification is applied only to the selected set of faces.&#xa; Take care of the target number of faces!""/>
 </filter>
 <filter name=""Re-Compute Vertex Normals"">
  <Param enum_val1=""By Angle"" enum_val3=""As defined by N. Max"" value=""0"" description=""Weighting Mode:"" type=""RichEnum"" enum_cardinality=""4"" enum_val2=""By Area"" name=""weightMode"" enum_val0=""None (avg)"" tooltip=""""/>
 </filter>
</FilterScript>
";
            string meshlabScript = string.Format(script, numSamples, deblobRadius, targetFaces);
            MeshLabOutputAttributes attributes = new MeshLabOutputAttributes(normals: m.HasNormals, uvs: false, colors: false);
            Mesh result = null;
            // This meshlab script fails to produce an output some small precentage of the time
            // Retry up to 3 times before giving up
            int maxTries = 3;
            for (int i = 1; i <= maxTries; i++)
            {
                try
                {
                    MeshLabRunner mlr = new MeshLabRunner(m, meshlabScript, attributes, ".obj", ".obj");
                    result = mlr.Run();
                    // No exception, break so we don't retry
                    break;
                }
                catch (MeshLabException e)
                {
                    logger.Warn(string.Format("Meshlab error.  Retry attempt {0}/{1}", i, maxTries), e);
                    if (i == maxTries)
                    {
                        throw(e);
                    }
                }
            }
            return Mesh.Clip(result, bounds);
        }
        
        /// <summary>
        /// Compute the largest geometric bi-directional difference between two meshes
        /// Works on meshes with faces and point sets
        /// </summary>
        public static HausdorffDistanceStats BidirectionalHausdorffDistance(Mesh m1, Mesh m2, double maxDist = 0, int numSamples = 250000)
        {
            string script = @"<!DOCTYPE FilterScript>
<FilterScript>
 <filter name=""Hausdorff Distance"">
  <Param description=""Sampled Mesh"" value=""1"" name=""SampledMesh"" tooltip=""The mesh whose surface is sampled. For each sample we search the closest point on the Target Mesh."" type=""RichMesh""/>
  <Param description=""Target Mesh"" value=""0"" name=""TargetMesh"" tooltip=""The mesh that is sampled for the comparison."" type=""RichMesh""/>
  <Param description=""Save Samples"" value=""false"" name=""SaveSample"" tooltip=""Save the position and distance of all the used samples on both the two surfaces, creating two new layers with two point clouds representing the used samples."" type=""RichBool""/>
  <Param description=""Sample Vertexes"" value=""true"" name=""SampleVert"" tooltip=""For the search of maxima it is useful to sample vertices and edges of the mesh with a greater care. It is quite probably the the farthest points falls along edges or on mesh vertexes, and with uniform montecarlo sampling approachesthe probability of taking a sample over a vertex or an edge is theoretically null.&lt;br>On the other hand this kind of sampling could make the overall sampling distribution slightly biased and slightly affects the cumulative results."" type=""RichBool""/>
  <Param description=""Sample Edges"" value=""true"" name=""SampleEdge"" tooltip=""See the above comment."" type=""RichBool""/>
  <Param description=""Sample FauxEdge"" value=""false"" name=""SampleFauxEdge"" tooltip=""See the above comment."" type=""RichBool""/>
  <Param description=""Sample Faces"" value=""true"" name=""SampleFace"" tooltip=""See the above comment."" type=""RichBool""/>
  <Param description=""Number of samples"" value=""{0}"" name=""SampleNum"" tooltip=""The desired number of samples. It can be smaller or larger than the mesh size, and according to the choosed sampling strategy it will try to adapt."" type=""RichInt""/>
  <Param description=""Max Distance"" value=""{1}"" name=""MaxDist"" tooltip=""Sample points for which we do not find anything whithin this distance are rejected and not considered neither for averaging nor for max."" min=""0"" max=""710.981"" type=""RichAbsPerc""/>
 </filter>
</FilterScript>
";
            // If max dist is 0 set it to 10% by default
            if (maxDist == 0)
            {
                maxDist = Vector3.Max(m1.Bounds().Extent(), m2.Bounds().Extent()).Length() * 0.1f;
            }
            
            string meshlabScript = string.Format(script, numSamples, maxDist);
            MeshLabOutputAttributes attributes = new MeshLabOutputAttributes(false, false, false);
            MeshLabRunner forwardMLR = new MeshLabRunner(new List<Mesh>(new Mesh[] { m1, m2 }), meshlabScript, attributes, ".ply", null);
            forwardMLR.Run();
            
            var forwardDistance = HausdorffDistanceStats.ParseFromMeshLabOutput(forwardMLR.OutputText);
            MeshLabRunner backwardMLR = new MeshLabRunner(new List<Mesh>(new Mesh[] { m2, m1 }), meshlabScript, attributes, ".ply", null);
            backwardMLR.Run();
            var backwardDistance = HausdorffDistanceStats.ParseFromMeshLabOutput(backwardMLR.OutputText);
            return HausdorffDistanceStats.Largest(forwardDistance, backwardDistance);
        }
    }
}
