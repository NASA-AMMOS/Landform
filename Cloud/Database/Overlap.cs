using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Cloud
{
    //
    class Overlap
    {
        public int Id { get; set; }

        public Observation obs1 { get; set; }

        public Observation obs2 { get; set; }

        //S3 location of keypoint map 
        public string keypoints { get; set; }
    }
}
