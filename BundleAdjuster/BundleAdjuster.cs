using System;
using System.Collections.Generic;
using CommandLine;
using OPS.Util;
using OPS.Pipeline;

namespace OPS.Landform
{
    class BundleAdjuster
    {
        static int Main(string[] args)
        {
            if (!CommandHelper.Configure(args, typeof(BundleAdjuster), typeof(PipelineCore),
                                         () => CommandHelper.HasFlag(args, "cloud") ?
                                         CloudPipelineConfig.Instance.ConfigFilePath() :
                                         LocalPipelineConfig.Instance.ConfigFilePath()))
            {
                return 1;
            }

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
                    { typeof(DetectFeaturesOptions), typeof(DetectFeatures) },
                    { typeof(MatchFeaturesOptions), typeof(MatchFeatures) },
                    { typeof(BundleAdjustAlignerOptions), typeof(BundleAdjustAligner) },
                };

            return CommandHelper.RunFromCommandline(args, verbs);
        }
    }
}
