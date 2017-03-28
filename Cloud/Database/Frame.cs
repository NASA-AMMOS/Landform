using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Cloud
{

    /// <summary>
    /// Represents a coordinate frame in the database
    /// Coordiante frames can have one or more observations associated with them
    /// </summary>
    public class Frame
    {
        public int Id { get; set; }
        [Required]
        public int ProjectId { get; set; }
        public string Name { get; set; }

        public Frame()
        {

        }

        public Frame(int projectId, string name)
        {
            this.Name = name;
            this.ProjectId = projectId;
        }
    }
}
