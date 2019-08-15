using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;

namespace OPS.Imaging
{
    public interface ISpatialTranslator
    {
        Vector3 ProjectedToWorld(Vector3 projected);
        Vector3 WorldToProjected(Vector3 world);   
    }
}
