using System;
using System.Collections.Generic;
using CommandLine;
using OPS.Util;

namespace OPS.LandformUtil
{
    class LandformUtil
    {
        static int Main(string[] args)
        {
            CommandHelper.Configure(args, "LandformUtil");

            //MeshSerializers in the OPS.Geometry subproject will auto-register themselves
            //in the static initializer for the OPS.Geometry.MeshSerializers SerializerMap
            //however there are also some additional MeshSerializers in OPS.GeometryThirdParty
            //and we also want those to add themselves to the OPS.Geometry.MeshSerializers SerializerMap
            OPS.Geometry.ThirdPartyMeshSerializers.Register();

            //use centralized version from OPS.Pipeline
            //not GdalConfiguration which is auto-added to this subproject by nuget
            OPS.Pipeline.GdalConfiguration.ConfigureGdal();

            var verbs = new Dictionary<Type, Type>()
                {
                    { typeof(LocalObservationProductsOptions), typeof(LocalObservationProducts) },
                    { typeof(ConvertPDSOptions), typeof(ConvertPDS) },
                    { typeof(DEM2MeshOptions), typeof(DEM2Mesh) },
                    { typeof(BenchmarkS3Options), typeof(BenchmarkS3) },
                    { typeof(LimberDMGOptions), typeof(LimberDMGDriver) },
                    { typeof(ConvertToASTTROOptions), typeof(ConvertToASTTRO) },
                    { typeof(UpdateSceneManifestOptions), typeof(UpdateSceneManifest) },
                };

            return CommandHelper.RunFromCommandline(args, verbs);
        }
    }
}
