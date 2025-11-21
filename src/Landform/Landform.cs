using System;
using System.Collections.Generic;
using JPLOPS.Util;
using JPLOPS.Pipeline;

namespace JPLOPS.Landform
{
    class Landform
    {
        static int Main(string[] args)
        {
            try
            {
                if (!CommandHelper.Configure(args, typeof(Landform), typeof(PipelineCore),
                                             () => LocalPipelineConfig.Instance.ConfigFilePath()))
                {
                    return 1;
                }
                
                JPLOPS.Imaging.GdalConfiguration.ConfigureGdal();
                
                var verbs = new Dictionary<Type, Type>()
                {
                    { typeof(ConfigureOptions), typeof(Configure) },
                    
                    { typeof(FetchDataOptions), typeof(FetchData) },
                    
                    { typeof(IngestOptions), typeof(Ingest) },
                    
                    { typeof(BEVAlignerOptions), typeof(BEVAligner) },
                    { typeof(HeightmapAlignerOptions), typeof(HeightmapAligner) },
                    
                    { typeof(BuildGeometryOptions), typeof(BuildGeometry) },
                    { typeof(BuildSkySphereOptions), typeof(BuildSkySphere) },
                    { typeof(BuildTextureOptions), typeof(BuildTexture) },
                    { typeof(BuildTilingInputOptions), typeof(BuildTilingInput) },
                    { typeof(BuildTilesetOptions), typeof(BuildTileset) },
                    
                    { typeof(BlendImagesOptions), typeof(BlendImages) },
                    { typeof(LimberDMGOptions), typeof(LimberDMGDriver) },
                    
                    { typeof(ProcessTacticalOptions), typeof(ProcessTactical) },
                    { typeof(ProcessContextualOptions), typeof(ProcessContextual) },
                    
                    { typeof(UpdateSceneManifestOptions), typeof(UpdateSceneManifest) },
                    { typeof(ObservationProductsOptions), typeof(ObservationProducts) },
                    
                    { typeof(ConvertPDSOptions), typeof(ConvertPDS) },
                    { typeof(ConvertIVOptions), typeof(ConvertIV) },
                    { typeof(ConvertGLTFOptions), typeof(ConvertGLTF) },
                    { typeof(DEM2MeshOptions), typeof(DEM2Mesh) },
                    
                    { typeof(BenchmarkS3Options), typeof(BenchmarkS3) },
                };
                
                return CommandHelper.RunFromCommandline(args, verbs);
            }
            catch (Exception ex)
            {
                Config.Log("unhandled exception: " + ex.ToString());
                if (Config.Logger == null)
                {
                    Config.SetLogger(new ThunkLogger(info: msg => Console.Error.WriteLine(msg)));
                }
                return 1;
            }
        }
    }
}
