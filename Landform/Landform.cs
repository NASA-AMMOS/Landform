using System;
using System.Collections.Generic;
using CommandLine;
using OPS.Util;

namespace OPS.Landform
{
    class Landform
    {
        static int Main(string[] args)
        {
            CommandHelper.Configure(args, "Landform");

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
                    { typeof(ConfigureCloudOptions), typeof(ConfigureCloud) },
                    { typeof(ConfigureLocalOptions), typeof(ConfigureLocal) },
                    { typeof(FetchDataOptions), typeof(FetchData) },
                    { typeof(IngestOptions), typeof(Ingest) },
                    { typeof(BEVAlignerOptions), typeof(BEVAligner) },
                    { typeof(HeightmapAlignerOptions), typeof(HeightmapAligner) },
                    { typeof(AgisoftAlignerOptions), typeof(AgisoftAligner) },
                    { typeof(BuildGeometryOptions), typeof(BuildGeometry) },
                    { typeof(BuildTilesetOptions), typeof(BuildTileset) },
                    { typeof(BuildTextureOptions), typeof(BuildTexture) },
                    { typeof(BuildTilingInputOptions), typeof(BuildTilingInput) },
                    { typeof(BlendImagesOptions), typeof(BlendImages) },
                    { typeof(ProcessTacticalOptions), typeof(ProcessTactical) },
                };

            return CommandHelper.RunFromCommandline(args, verbs);
        }
    }
}
