using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Imaging
{

    public class PDSFieldReader
    {
        public CameraModel CameraModel { get; private set; }
        
        public PDSFieldReader(PDSMetadata metadata)
        {
            CameraModel = ParseCameraModel(metadata);   
        }

        CameraModel ParseCameraModel(PDSMetadata metadata)
        {
            return null;
        }
    } 

}