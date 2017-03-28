using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Cloud
{
    public static class ObservationType
    {
        public const string Image = "image";
        public const string Range = "range";
    }

    /// <summary>
    /// Represents an image or 3D shape measurement of the environment
    /// Can be connected to Frames and aligned with other observations through
    /// FrameTransforms
    /// </summary>
    public class Observation
    {
        public int Id { get; set; }

        [Required]
        public string Url { get; set; }

        [Required]
        public int ProjectId { get; set; }

        [Required]
        public int FrameId { get; set; }

        [Required]
        public string Name { get; set; }

        [Required]
        public string ObservationType { get; set; }

        public string CameraModel { get; set; }

        public Observation()
        {

        }

        public Observation(int projectId, int frameId, string url, string name, string observationType, string cameraModel)
        {
            this.ProjectId = projectId;
            this.FrameId = frameId;
            this.Url = url;
            this.Name = name;
            this.ObservationType = observationType;
            this.CameraModel = cameraModel;
        }
    }
}
