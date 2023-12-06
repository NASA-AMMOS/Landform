using System;

namespace ImagingTest.Reference
{
    public class CAHV
    {

        public const double EPSILON = 1e-15;

        protected double[] c;
        protected double[] a;
        protected double[] h;
        protected double[] v;

        public CAHV(double[] c, double[] a, double[] h, double[] v) 
        {
            this.c = c;
            this.a = a;
            this.h = h;
            this.v = v;
        }

        public double[] C { get { return c; } }
        public double[] A { get { return a; } }
        public double[] H { get { return h; } }
        public double[] V { get { return v; } }

        /******************************************************************************
        ********************************   CMOD_CAHV_2D_TO_3D   ***********************
        *******************************************************************************

        This function projects a 2D image point out into 3D using the
        camera model parameters provided. In addition to the 3D projection,
        it outputs and the partial-derivative matrix of the unit vector of the
        projection with respect to the 2D image-plane point. If the parameter
        for the output partial matrix is passed as (cmod_float_t (*)[2])NULL,
        then it will not be calculated. */

        public void cmod_cahv_2d_to_3d(double[] pos2,	/* input 2D position */
            double[] pos3, /* output 3D origin of projection */
            double[] uvec3)    /* output unit vector ray of projection */
        {
            double[] f = new double[3];
            double[] g = new double[3];
            double magi;
            double[] t = new double[3];

            /* The projection point is merely the C of the camera model */
            copy3(c, pos3);

            /* Calculate the projection ray assuming normal vector directions */
            scale3(pos2[1], a, f);
            sub3(v, f, f);
            scale3(pos2[0], a, g);
            sub3(h, g, g);
            cross3(f, g, uvec3);
            magi = mag3(uvec3);
            if (magi <= EPSILON)
                System.Console.WriteLine("warning: magi is too small");
            magi = 1.0 / magi;
            scale3(magi, uvec3, uvec3);

            cross3(v, h, t);
            if (dot3(t, a) < 0)
            {
                scale3(-1.0, uvec3, uvec3);
            }
        }

        public virtual void Project_3d_to_2d( double[] pos3,	/* input 3D position */
            out double range,	/* output range along A (same units as C). */
            double[] pos2)
        {
            cmod_cahv_3d_to_2d(pos3, out range, pos2);
        }

        public virtual void Project_2d_to_3d(double[] pos2, double[] pos3, double[] uvec3)
        {
            cmod_cahv_2d_to_3d(pos2, pos3, uvec3);
        }

        /******************************************************************************
        ********************************   CMOD_CAHV_3D_TO_2D   ***********************
        *******************************************************************************

            This function projects a 3D point into the image plane using the
            camera model parameters provided. In addition to the 2D projection,
            it outputs the 3D perpendicular distance from the camera to the
            3D point, and the partial derivative matrix of the 2D point with respect
            to the 3D point. If the parameter for the output partial matrix is
            passed as (cmod_float_t (*)[3])NULL, then it will not be calculated. */

        public void cmod_cahv_3d_to_2d(
            double[] pos3,	/* input 3D position */
            out double range,	/* output range along A (same units as C). */
            double[] pos2)	/* output 2D image-plane projection */
        {
            double[] d = new double[3];
            double r_1;

           /* Calculate the projection */
            sub3(pos3, c, d);
            range = dot3(d, a);
            if (Math.Abs(range) <= EPSILON)
                System.Console.WriteLine("Warning: range is too small");
            r_1 = 1.0 / range;
            pos2[0] = dot3(d, h) * r_1;
            pos2[1] = dot3(d, v) * r_1;
        }

        public static void copy3(double[] a, double[] b)
        {
            /* Copy the two vectors */
            b[0] = a[0];
            b[1] = a[1];
            b[2] = a[2];
        }

        public static void scale3(double s, double[] a, double[] b)
        {
            /* Perform the scalar multiplication */
            b[0] = s * a[0];
            b[1] = s * a[1];
            b[2] = s * a[2];
        }

        public static void sub3(double[] a, double[] b, double[] c)
        {
            /* Subtract the two vectors */
            c[0] = a[0] - b[0];
            c[1] = a[1] - b[1];
            c[2] = a[2] - b[2];
        }
        public static void add3(double[] a, double[] b, double[] c)
        {
            /* Add the two vectors */
            c[0] = a[0] + b[0];
            c[1] = a[1] + b[1];
            c[2] = a[2] + b[2];
        }

        public static void cross3(double[] a, double[] b, double[] c)
        {
            double[] d = new double[3];
            /* Perform the cross product */
            d[0] = a[1] * b[2] - a[2] * b[1];
            d[1] = a[2] * b[0] - a[0] * b[2];
            d[2] = a[0] * b[1] - a[1] * b[0];

            /* Return a pointer to the result */
            c[0] = d[0];
            c[1] = d[1];
            c[2] = d[2];
        }

        public static double mag3(double[] a)
        {
            /* Calculate the magnitude */
            return Math.Sqrt(a[0] * a[0] + a[1] * a[1] + a[2] * a[2]);
        }

        public static double dot3(double[] a, double[] b)
        {
            /* Dot the two vectors */
            double f = a[0] * b[0] +
            a[1] * b[1] +
            a[2] * b[2];

            /* Return the dot product */
            return f;
        }

        public static double[] unit3(double[] a, double[] b)
        {
            double mag = Math.Sqrt(a[0] * a[0] + a[1] * a[1] + a[2] * a[2]);
            if (mag < EPSILON)
                return null;

            /* Convert to a unit vector */
            b[0] = a[0] / mag;
            b[1] = a[1] / mag;
            b[2] = a[2] / mag;

            /* Return a pointer to the result */
            return b;
        }
    }
}
