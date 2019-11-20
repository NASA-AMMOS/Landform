using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;
using OPS.Util;
using OPS.Pipeline;

namespace OPS.TilingServer
{
    class TilingServer
    {
        static int Main(string[] args)
        {
            CommandHelper.Init(args, "TilingServer");

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
                    { typeof(CreateProjectOptions), typeof(CreateProject) },
                    { typeof(UploadInputOptions), typeof(UploadInput) },
                    { typeof(RunProjectOptions), typeof(RunProject) },
                    { typeof(StartWorkerOptions), typeof(StartWorker) },
                    { typeof(StartMasterOptions), typeof(StartMaster) },
                    { typeof(ProjectMetadataOptions), typeof(ProjectMetadata) },
                    { typeof(ListProjectsOptions), typeof(ListProjects) },
                    { typeof(DeleteProjectOptions), typeof(DeleteProject) },
                    { typeof(DeleteQueuesOptions), typeof(DeleteQueues) },
                    { typeof(DeleteCacheOptions), typeof(DeleteCache) },
                };

            return CommandHelper.RunFromCommandline(args, verbs);
        }
    }
}
