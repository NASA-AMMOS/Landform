using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace JPLOPS.Geometry
{
    /// <summary>
    /// This is a class for fitting points to a plane based on
    /// https://www.ilikebigbits.com/2015_03_04_plane_from_points.html
    /// There are more robust approaches such as
    /// https://github.jpl.nasa.gov/MIPL/Vicar_dev/blob/cdbd1036da34dfb5456aeb3dc463ae6e0a097a57/VICAR/mars/src/prog/marsuvw/xyz_to_uvw.cc
    /// </summary>
    public class PlaneFit
    {
        public Vector3 Centroid;
        public Vector3 Normal;

        public PlaneFit(Vector3 c, Vector3 n)
        {
            Centroid = c;
            Normal = n;
        }

        public PlaneFit(List<Vertex> points)
        {
            var r = FitPlane(points);
            if (r == null)
            {
                return;
            }
            this.Centroid = r.Centroid;
            this.Normal = r.Normal;
        }

        static PlaneFit FitPlane(List<Vertex> points)
        {

            if (points.Count < 3)
            {
                return null; // At least three points required
            }

            var sum = new Vector3();
            foreach (var p in points)
            {
                sum += p.Position;
            }
            var centroid = sum * (1.0 / points.Count());

            // Calc full 3x3 covariance matrix, excluding symmetries:
            var xx = 0.0; var xy = 0.0; var xz = 0.0;
            var yy = 0.0; var yz = 0.0; var zz = 0.0;

            foreach (var p in points)
            {
                var r = p.Position - centroid;
                xx += r.X * r.X;
                xy += r.X * r.Y;
                xz += r.X * r.Z;
                yy += r.Y * r.Y;
                yz += r.Y * r.Z;
                zz += r.Z * r.Z;
            }

            var det_x = yy * zz - yz * yz;
            var det_y = xx * zz - xz * xz;
            var det_z = xx * yy - xy * xy;

            var det_max = Math.Max(det_x, Math.Max(det_y, det_z));
            if (det_max <= 0.0)
            {
                return null; // The points don't span a plane
            }

            // Pick path with best conditioning:
            var dir = Vector3.Zero;
            if (det_max == det_x)
            {
                dir = new Vector3(det_x, xz * yz - xy * zz, xy * yz - xz * yy);

            }
            else if (det_max == det_y)
            {
                dir = new Vector3(xz * yz - xy * zz, det_y, xy * xz - yz * xx);
            }
            else
            {
                dir = new Vector3(xy * yz - xz * yy, xy * xz - yz * xx, det_z);
            }

            return new PlaneFit(centroid, Vector3.Normalize(dir));
        }
    }
}
