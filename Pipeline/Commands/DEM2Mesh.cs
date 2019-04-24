using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using OPS.Util;
using OPS.Imaging;
using OPS.Geometry;
using Microsoft.Xna.Framework;
using CommandLine;

namespace OPS.Pipeline
{

    [Verb("dem2mesh", HelpText = "Convert a dem and optional ortho image to a mesh.  X North (points toward top of dem image space), Y up (values from dem), Z East (points right in dem image space)")]
    public class DEM2MeshOptions
    {
        [Value(0, Required = true, Default = 1, HelpText = "Size of a pixel in the DEM in meters")]
        public double MetersPerPixel { get; set; }

        [Value(1, Required = true, HelpText = "Image containing heights as values")]
        public string InputDem { get; set; }

        [Value(2, Required = false, HelpText = "Optional input ortho image.  The image must be the same aspect and physical extent as the DEM, but can have a different resolution.")]
        public string InputOrthoImage { get; set; }

        [Option(Required = false, Default = 1, HelpText = "Scaler to convert dem  values to verticle meters.  i.e. (meters/pixel value)")]
        public float VerticleScale { get; set; }

        [Option(Required = false, HelpText = "Output path of mesh.  If ortho image is supplied it will be written to the same path but with a different extension.  If ommited output is written to same directory as input but with a '.mesh' appended to the filename")]
        public string OutputPath { get; set; }
        
        [Option(Required = false, Default = "png", HelpText = "Export format for textures (examples: jpg or png")]
        public string ImageFormat { get; set; }

        [Option(Required = false, Default = "obj", HelpText = "Export format for mesh (examples: obj or ply")]
        public string MeshFormat { get; set; }

        [Option(Required = false, Default=1, HelpText = "If specified, decimate the output mesh by this ration, 1 = no decimaton, 0 = empty mesh")]
        public float DecimationRatio { get; set; }

        [Option(Required = false, Default = 0, HelpText = "If specified, decimate the output mesh to the target number of faces")]
        public int TargetFaces { get; set; }

        [Option(Required = false, Default = false, HelpText = "Do not allow decimation to modify the edge")]
        public bool MaintainEdge { get; set; }

        [Option(Required = false, Default = 1, HelpText = "Set higher to reduce the amount of decimation applied to edge vertices")]
        public float EdgeWeight { get; set; }

        [Option(Required = false, Default = -1000000, HelpText = "Dem values less than this will be ignored")]
        public float DEMMinFilter { get; set; }

        [Option(Required = false, Default = 1000000, HelpText = "Dem values larger than this will be ignored")]
        public float DEMMaxFilter { get; set; }

        // TODO: Skirt option?
    }

    public class DEM2Mesh
    {
        DEM2MeshOptions options;

        public DEM2Mesh(DEM2MeshOptions options)
        {
            this.options = options;
        }

        public int Run()
        {
            if(!string.IsNullOrEmpty(this.options.OutputPath))
            {
                PathHelper.EnsureExists(this.options.OutputPath);
            }
            else 
            {
                this.options.OutputPath = Path.Combine(Path.GetDirectoryName(options.InputDem), Path.GetFileNameWithoutExtension(options.InputDem) + ".mesh." + options.MeshFormat);
            }
            
            Image dem = Image.Load(options.InputDem, ImageConverters.PassThrough);
            
            if(dem.CameraModel == null)
            {
                dem.CameraModel = new OrthographicCameraModel(Matrix.Identity, dem.Width, dem.Height, options.MetersPerPixel);
            }
           
            Image xyz = null;
            if(dem.Bands == 3)
            {
                xyz = dem;  // Unusual but handle the case where we are passed a 3 band xyz image instead of a dem
            }
            else
            {
                dem.ScaleValues(options.VerticleScale);
                xyz = Meshing.ConvertRNG(dem, null);
            }
            Image mask = new Image(1, dem.Width, dem.Height);
            foreach (var coord in dem.Coordinates(true))
            {
                var value = dem[coord.Band, coord.Row, coord.Col];
                mask[0, coord.Row, coord.Col] = value >= options.DEMMinFilter && value <= options.DEMMaxFilter ? 1 : 0;
            }
            var mesh = Meshing.BuildOrganizedMesh(xyz, mask: mask);
            mesh.GenerateVertexNormals();
            bool targetFacesDefined = options.TargetFaces != 0;
            bool decimationRatioDefined = options.DecimationRatio != 0 && options.DecimationRatio != 1;
            if(targetFacesDefined || decimationRatioDefined)
            {
                var faceTarget = targetFacesDefined ? options.TargetFaces : mesh.Faces.Count;
                if(decimationRatioDefined)
                {
                    faceTarget = (int)Math.Min(MathHelper.Clamp(options.DecimationRatio, 0, 1) * mesh.Faces.Count, faceTarget);
                }
                var notToched = mesh.Corners(new Vector3(0,1,0));
                if(options.MaintainEdge)
                {
                    notToched = mesh.EdgeVertices();
                }
                //mesh = MeshLab.Decimate(mesh, faceTarget);
                //mesh.ClearUVs();
                //mesh.ResampleDecimation(MeshReconMethod.Poisson, faceTarget, mesh.Bounds(), new Vector3(0, 1, 0));
                mesh = EdgeCollapse.QuadricEdgeCollapse(mesh, faceTarget, perimeterPenaltyFactor: options.EdgeWeight, notTouched: notToched);
                // TODO: re-atlas
            }
            foreach(var v in mesh.Vertices)
            {
                v.Position *= 0.01;
            }
            string outputImage = null;
            if (options.InputOrthoImage != null)
            {
                Image ortho = Image.Load(options.InputOrthoImage);
                outputImage = Path.Combine(Path.GetDirectoryName(options.InputDem), Path.GetFileNameWithoutExtension(options.InputDem) + ".mesh." + options.ImageFormat);
                ortho.Save<byte>(outputImage); // TODO, add support for matching input type
            }
            mesh.Save(this.options.OutputPath, outputImage);
            return 0;
        }

    }

    
    
}
