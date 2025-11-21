using System;

namespace ImagingTest.Reference
{
    public class CAHVOR : CAHV
    {
        protected double[] o;
        protected double[] r;

        public CAHVOR(double[] c, double[] a, double[] h, double[] v, double[] o, double[] r): base(c, a, h, v)
        {
            this.o = o; this.r = r;
        }

        public double[] O { get { return o; } }
        public double[] R { get { return r; } }

        public override void Project_3d_to_2d(double[] pos3,	/* input 3D position */
            out double range,	/* output range along A (same units as C). */
            double[] pos2)
        {
            cmod_cahvor_3d_to_2d(pos3, out range, pos2);
        }

        public override void Project_2d_to_3d(double[] pos2, double[] pos3, double[] uvec3)
        {
            cmod_cahvor_2d_to_3d(pos2, pos3, uvec3);
        }

        /******************************************************************************
        ********************************   CMOD_CAHVOR_3D_TO_2D   *********************
        *******************************************************************************

            This function projects a 3D point into the image plane using the
            camera model parameters provided. In addition to the 2D projection,
            it outputs the 3D perpendicular distance from the camera to the
            3D point, and the partial derivative matrix of the 2D point with respect
            to the 3D point. If the parameter for the output partial matrix is
            passed as (cmod_float_t (*)[3])NULL, then it will not be calculated. */

        public void cmod_cahvor_3d_to_2d(
            double[] pos3,	/* input 3D position */
            out double range,	/* output range along A (same units as C). */
            double[] pos2)	/* output 2D image-plane projection */
        {
            double alpha;
            double beta;
            double gamma;
            double[] lambda = new double[3];
            double mu;
            double omega;
            double omega_2;
            double[] p_c = new double[3];
            double[] pp = new double[3];
            double[] pp_c = new double[3];
            double tau;
            double[] wo = new double[3];
            double xh;
            double yh;

            /* Calculate p' and other necessary quantities */
            sub3(pos3, c, p_c);
            omega = dot3(p_c, o);
            omega_2 = omega * omega;
            if (Math.Abs(omega_2) <= EPSILON)
                Console.WriteLine("warning: omega_2 is too small");
            scale3(omega, o, wo);
            sub3(p_c, wo, lambda);
            tau = dot3(lambda, lambda) / omega_2;
            mu = r[0] + (r[1] * tau) + (r[2] * tau * tau);
            scale3(mu, lambda, pp);
            add3(pos3, pp, pp);

            /* Calculate alpha, beta, gamma, which are (p' - c) */
            /* dotted with a, h, v, respectively                */
            sub3(pp, c, pp_c);
            alpha  = dot3(pp_c, a);
            beta   = dot3(pp_c, h);
            gamma  = dot3(pp_c, v);
            if (Math.Abs(alpha) <= EPSILON)
                Console.WriteLine("warning: alpha is too small");

            /* Calculate the projection */
            pos2[0] = xh = beta  / alpha;
            pos2[1] = yh = gamma / alpha;
            range = alpha;
        }

        const int MAXITER = 20;
        const float CONV = 1.0e-6f;

        /******************************************************************************
        ********************************   CMOD_CAHVOR_2D_TO_3D   *********************
        *******************************************************************************
 
             This function projects a 2D image point out into 3D using the
             camera model parameters provided. In addition to the 3D projection,
             it outputs the partial-derivative matrix of the unit vector of the
             projection with respect to the 2D image-plane point.  If the parameter
             for the output partial matrix is passed as (double (*)[2])NULL, then it
             will not be calculated. */

        public void cmod_cahvor_2d_to_3d(
            double[] pos2,  // Input 2d position
            double[] pos3,  // Output 3d origin of projection
            double[] uvec3) // Output unit vector of projection
        {
            /* The projection point is merely the C of the camera model. */
            copy3(c, pos3);

            /* Calculate the projection ray assuming normal vector directions, */
            /* neglecting distortion.                                          */
            double[] f = new double[3];
            double[] g = new double[3];
            double[] rr = new double[3];
            double[] t = new double[3];
            double magi;

            scale3(pos2[1], a, f);
            sub3(v, f, f);
            scale3(pos2[0], a, g);
            sub3(h, g, g);
            cross3(f, g, rr);
            magi = 1.0 / mag3(rr);
            scale3(magi, rr, rr);

            cross3(v, h, t);
            if (dot3(t, a) < 0)
            {
                scale3(-1.0, rr, rr);
            }
           
            /* Remove the radial lens distortion.  Preliminary values of omega,  */
            /* lambda, and tau are computed from the rr vector including         */
            /* distortion, in order to obtain the coefficients of the equation   */
            /* k5*u^5 + k3*u^3 + k1*u = 1, which is solved for u by means of     */
            /* Newton's method.  This value is used to compute the corrected rr. */
            double omega, omega_2;
            double[] wo = new double[3];
            double[] lambda = new double[3];
            double tau;
            double k1, k3, k5;
            double mu, u, u_2;

            omega = dot3(rr, o);
            omega_2 = omega * omega;
            scale3(omega, o, wo);
            sub3(rr, wo, lambda);
            tau = dot3(lambda, lambda) / omega_2;
            k1 = 1 + r[0];              /*  1 + rho0 */
            k3 = r[1] * tau;            /*  rho1*tau  */
            k5 = r[2] * tau * tau;      /*  rho2*tau^2  */
            mu = r[0] + k3 + k5;
            u = 1.0 - mu;       /* initial approximation for iterations */
            int i;
            for (i = 0; i < MAXITER; i++)
            {
                double poly, deriv, du;
                u_2 = u * u;
                poly = ((k5 * u_2 + k3) * u_2 + k1) * u - 1;
                deriv = (5 * k5 * u_2 + 3 * k3) * u_2 + k1;
                if (deriv <= 0)
                {
                    Console.WriteLine("cmod_cahvor_2d_to_3d(): Distortion is too negative\n");
                    break;
                }
                else
                {
                    du = poly / deriv;
                    u -= du;
                    if (Math.Abs(du) < CONV)
                        break;
                }
            }
            if (i >= MAXITER)
                Console.WriteLine("cmod_cahvor_2d_to_3d(): Too many iterations ({0})\n", i);

            double[] pp = new double[3];
            double magv;

            mu = 1 - u;
            scale3(mu, lambda, pp);
            sub3(rr, pp, uvec3);
            magv = mag3(uvec3);
            scale3(1.0 / magv, uvec3, uvec3);
        }
    }
}
