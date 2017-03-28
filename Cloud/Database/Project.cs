using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Cloud
{
    /// <summary>
    /// A project specifies a container for a 3D reconstruction consiting of mutliple observations
    /// </summary>
    public class Project
    {
        public int Id { get; set; }
        [Required]
        public string Name { get; set; }
        
        public Project()
        {

        }

        public Project(string name)
        {
            this.Name = name;
        }
    }
}
