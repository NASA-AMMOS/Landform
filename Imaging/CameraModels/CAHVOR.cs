using System;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;

namespace JPLOPS.Imaging
{
    public class CAHVOR : CAHV
    {
        public const int MAXITER = 20;
        public const float CONV = 1.0e-6f;

        public Vector3 O;
        public Vector3 R;

        [JsonIgnore]
        public override bool Linear { get { return false; } }

        public CAHVOR() { }

        public CAHVOR(Vector3 c, Vector3 a, Vector3 h, Vector3 v, Vector3 o, Vector3 r) : base(c,a,h,v)
        {
            this.O = o;
            this.R = r;
        }

        public CAHVOR(CAHVOR that) : base(that)
        {
            this.O = that.O;
            this.R = that.R;
        }

        /// <summary>
        /// Port from Todd's CMOD_CAHVOR_2D_TO_3D
        /// </summary>
        /// <param name="pixelPos"></param>
        /// <param name="range"></param>
        /// <returns></returns>
        public override void Unproject(ref Vector2 pixelPos, out Ray ray)
        {
            Vector3 pos3;
            Vector3 uvec3;
            /* The projection point is merely the C of the camera model. */
            pos3 = C;                               // copy3(c, pos3);

            /* Calculate the projection ray assuming normal vector directions, */
            /* neglecting distortion.                                          */
            Vector3 f = new Vector3();
            Vector3 g = new Vector3();
            Vector3 rr = new Vector3();
            Vector3 t = new Vector3();
            double magi;

            f = pixelPos.Y * A;                     // scale3(pixelPos[1], a, f);
            f = V - f;                              // sub3(v, f, f);
            g = pixelPos.X * A;                     // scale3(pixelPos[0], a, g);
            g = H - g;                              // sub3(h, g, g);
            rr = Vector3.Cross(f, g);               // cross3(f, g, rr);
            magi = rr.Length();
            if (magi <= EPSILON)
            {
                throw new CameraModelException("divide by zero");
            }
            magi = 1.0 / rr.Length();               // magi = 1.0 / mag3(rr);
            rr = magi * rr;                         // scale3(magi, rr, rr);

            t = Vector3.Cross(V, H);                // cross3(v, h, t);

            if (Vector3.Dot(t, A) < 0)              // if (dot3(t, a) < 0)  
            {
                rr = rr * -1;                       // scale3(-1.0, rr, rr);
            }

            /* Remove the radial lens distortion.  Preliminary values of omega,  */
            /* lambda, and tau are computed from the rr vector including         */
            /* distortion, in order to obtain the coefficients of the equation   */
            /* k5*u^5 + k3*u^3 + k1*u = 1, which is solved for u by means of     */
            /* Newton's method.  This value is used to compute the corrected rr. */
            double omega, omega_2;
            Vector3 wo = new Vector3();
            Vector3 lambda = new Vector3();
            double tau;
            double k1, k3, k5;
            double mu, u, u_2;

            omega = Vector3.Dot(rr, O);                     // dot3(rr, o); 
            omega_2 = omega * omega;
            wo = O * omega;                                 // scale3(omega, o, wo);
            lambda = rr - wo;                               // sub3(rr, wo, lambda);
            tau = Vector3.Dot(lambda, lambda) / omega_2;    // tau = dot3(lambda, lambda) / omega_2;
            k1 = 1 + R.X;                                   // k1 = 1 + r[0];              /*  1 + rho0 */
            k3 = R.Y * tau;                                 // k3 = r[1] * tau;            /*  rho1*tau  */
            k5 = R.Z * tau * tau;                           // k5 = r[2] * tau * tau;      /*  rho2*tau^2  */
            mu = R.X + k3 + k5;                             // mu = r[0] + k3 + k5;
            
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
                    throw new CameraModelException("cmod_cahvor_2d_to_3d(): Distortion is too negative");
                    //Console.WriteLine();
                    //break;
                }
                else
                {
                    du = poly / deriv;
                    u -= du;
                    if (Math.Abs(du) < CONV)
                        break;
                }
            }
            if (i >= MAXITER) {
                throw new CameraModelException("cmod_cahvor_2d_to_3d(): Too many iterations "+i);
                //Console.WriteLine("cmod_cahvor_2d_to_3d(): Too many iterations (%d)\n", i);
            }

            Vector3 pp = new Vector3();
            double magv;

            mu = 1 - u;
            pp = mu * lambda;               // scale3(mu, lambda, pp);
            uvec3 = rr-pp;                  // sub3(rr, pp, uvec3);
            magv = uvec3.Length();          // magv = mag3(uvec3);
            if(magv < EPSILON) {
                throw new CameraModelException("divide by zero");// not sure if this is possible but might as well check
            }
            uvec3 = uvec3 * (1.0 / magv);  // scale3(1.0 / magv, uvec3, uvec3);
                        
            ray = new Ray();
            ray.Position = pos3;
            ray.Direction = uvec3;
        }

        /// <summary>
        /// Port from Todd's CMOD_CAHVOR_3D_TO_2D
        /// </summary>
        /// <param name="pos"></param>
        /// <returns></returns>
        public override Vector2 Project(Vector3 pos, out double range)
        {
            Vector2 pos2 = new Vector2();
            double alpha;
            double beta;
            double gamma;
            Vector3 lambda = new Vector3();
            double mu;
            double omega;
            double omega_2;
            Vector3 p_c = new Vector3();
            Vector3 pp = new Vector3();
            Vector3 pp_c = new Vector3();
            double tau;
            Vector3 wo = new Vector3();
            double xh;
            double yh;

            /* Calculate p' and other necessary quantities */
            p_c = pos - C;                      // sub3(pos3, c, p_c);
            omega = Vector3.Dot(p_c, O);        // omega = dot3(p_c, o);
            omega_2 = omega * omega;
            if (Math.Abs(omega_2) <= EPSILON)
            {
                throw new CameraModelException("divide by zero");
                //Console.WriteLine("warning: omega_2 is too small");
            }
            wo = omega * O;                         // scale3(omega, o, wo);
            lambda = p_c - wo;                      // sub3(p_c, wo, lambda);
            tau = Vector3.Dot(lambda, lambda) / omega_2;    //tau = dot3(lambda, lambda) / omega_2;
            mu = R.X + (R.Y * tau) + (R.Z * tau * tau);     //mu = r[0] + (r[1] * tau) + (r[2] * tau * tau);
            pp = mu * lambda;                               //scale3(mu, lambda, pp);
            pp = pos + pp;                                  //add3(pos3, pp, pp);

            /* Calculate alpha, beta, gamma, which are (p' - c) */
            /* dotted with a, h, v, respectively                */
            pp_c = pp - C;                              //sub3(pp, c, pp_c);
            alpha = Vector3.Dot(pp_c, A);               //alpha = dot3(pp_c, a);
            beta = Vector3.Dot(pp_c, H);                //beta = dot3(pp_c, h);
            gamma = Vector3.Dot(pp_c, V);               //gamma = dot3(pp_c, v);
            if (Math.Abs(alpha) <= EPSILON)
            {
                throw new CameraModelException("divide by zero");
                //Console.WriteLine("warning: alpha is too small");
            }

            /* Calculate the projection */
            pos2.X = xh = beta / alpha;                 //pos2[0] = xh = beta / alpha;
            pos2.Y = yh = gamma / alpha;                //pos2[1] = yh = gamma / alpha;
            range = p_c.Length() * Math.Sign(alpha);
            return pos2;
        }

        public override object Clone()
        {
            return new CAHVOR(this);
        }
    }
}
