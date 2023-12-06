using System;
using Microsoft.Xna.Framework;

namespace JPLOPS.Imaging
{
    public class CAHVORE : CAHVOR
    {
        public const int MAX_NEWTON = 100;

        public const double PERSPECTIVE_LINEARITY = 1;
        public const double FISHEYE_LINEARITY = 0;

        public Vector3 E;

        public double Linearity;

        public CAHVORE() { }

        public CAHVORE(Vector3 c, Vector3 a, Vector3 h, Vector3 v, Vector3 o, Vector3 r, Vector3 e, double linearity)
            : base(c, a, h, v, o, r)
        {
            this.E = e;
            this.Linearity = linearity;
        }

        public CAHVORE(CAHVORE that) : base(that)
        {
            this.E = that.E;
            this.Linearity = that.Linearity;
        }

        /// <summary>
        /// Port from Todd's CMOD_CAHVOR_2D_TO_3D
        /// </summary>
        /// <param name="pixelPos"></param>
        /// <param name="range"></param>
        /// <returns></returns>
        public override void Unproject(ref Vector2 pixelPos, out Ray ray)
        {
            double zetap, lambdap, chip;
            double avh1;
            double chi, chi2, chi3, chi4, chi5;
            double linchi, theta, theta2, theta3, theta4;

            /* In the following there is a mixture of nomenclature from several */
            /* versions of Gennery's write-ups and Litwin's software. Beware!   */

            chi = 0;
            chi3 = 0;
            theta = 0;
            theta2 = 0;
            theta3 = 0;
            theta4 = 0;

            /* Calculate initial terms */

            Vector3 u3 = new Vector3();
            Vector3 v3 = new Vector3();
            Vector3 w3 = new Vector3();
            Vector3 rp = new Vector3();
            Vector3 lambdap3 = new Vector3();

            u3 = pixelPos.Y * A;                // scale3(pixelPos[1], a, u3);
            u3 = V - u3;                        // sub3(v, u3, u3);
            v3 = pixelPos.X * A;                // scale3(pixelPos[0], a, v3);
            v3 = H - v3;                        // sub3(h, v3, v3);
            w3 = Vector3.Cross(u3,v3);          // cross3(u3, v3, w3);
            u3 = Vector3.Cross(V, H);           // cross3(v, h, u3);
            double tmp = Vector3.Dot(A, u3);    // avh1 = 1 / dot3(a, u3);
            if (Math.Abs(tmp) < EPSILON)
            {
                throw new CameraModelException("divide by zero");  // Not sure if this is possible
            }
            avh1 = 1 / tmp;
            rp = avh1 * w3;                     // scale3(avh1, w3, rp);

            zetap = Vector3.Dot(rp, O);         // zetap = dot3(rp, o);

            u3 = zetap * O;                     // scale3(zetap, o, u3);
            lambdap3 = rp - u3;                 // sub3(rp, u3, lambdap3);

            lambdap = lambdap3.Length();        // lambdap = mag3(lambdap3);

            chip = lambdap / zetap;

            Vector3 cp = new Vector3();
            Vector3 ri = new Vector3();
            /* Approximations for small angles */
            if (chip < 1e-8)
            {
                cp = C;                         // copy3(c, cp);
                ri = O;                         // copy3(o, ri);
            }

            /* Full calculations */
            else
            {
                int n;
                double dchi, s;

                /* Calculate chi using Newton's Method */
                n = 0;
                chi = chip;
                dchi = 1;
                for (; ; )
                {
                    double deriv;

                    /* Make sure we don't iterate forever */
                    if (++n > MAX_NEWTON)
                    {
                        throw new CameraModelException("cahvore_2d_to_3d(): too many iterations");
                        //Console.WriteLine("cahvore_2d_to_3d(): too many iterations\n");
                        //break;
                    }

                    /* Compute terms from the current value of chi */
                    chi2 = chi * chi;
                    chi3 = chi * chi2;
                    chi4 = chi * chi3;
                    chi5 = chi * chi4;

                    /* Check exit criterion from last update */
                    if (Math.Abs(dchi) < 1e-8)
                        break;

                    /* Update chi */
                    deriv = (1 + R.X) + 3 * R.Y * chi2 + 5 * R.Z * chi4;
                    dchi = ((1 + R.X) * chi + R.Y * chi3 + R.Z * chi5 - chip) / deriv;
                    chi -= dchi;
                }
                /* Compute the incoming ray's angle */
                linchi = Linearity * chi;
                if (Linearity < -EPSILON)
                    theta = Math.Asin(linchi) / Linearity;
                else if (Linearity > EPSILON)
                    theta = Math.Atan(linchi) / Linearity;
                else
                    theta = chi;

                theta2 = theta * theta;
                theta3 = theta * theta2;
                theta4 = theta * theta3;

                /* Compute the shift of the entrance pupil */
                s = (theta / Math.Sin(theta) - 1) * (E.X + E.Y * theta2 + E.Z * theta4);

                /* The position of the entrance pupil */
                cp = s * O;                                 // scale3(s, o, cp);
                cp = C + cp;                                // add3(c, cp, cp);

                /* The unit vector along the ray */
                u3 = Vector3.Normalize(lambdap3);           // unit3(lambdap3, u3);
                u3 = Math.Sin(theta) * u3;                  // scale3(Math.Sin(theta), u3, u3);
                v3 = Math.Cos(theta) * O;                   // scale3(Math.Cos(theta), o, v3);
                ri = u3 + v3;                               // add3(u3, v3, ri);
            }
            ray = new Ray(cp, ri);                          // copy3(cp, pos3);
                                                            // copy3(ri, uvec3);
        }

        /// <summary>
        /// Port from Todd's CMOD_CAHVOR_3D_TO_2D
        /// </summary>
        /// <param name="pos"></param>
        /// <returns></returns>
        public override Vector2 Project(Vector3 pos, out double range)
        {
            int n;
            double zeta, lambda;
            double dtheta, theta, theta2, theta3, theta4;
            double upsilon, costh, sinth;
            double alpha, beta, gamma;

            /* In the following there is a mixture of nomenclature from several */
            /* versions of Gennery's write-ups and Litwin's software. Beware!   */

            upsilon = 0;
            costh = 0;
            sinth = 0;

            /* Basic Computations */

            Vector3 p_c = new Vector3();
            Vector3 u3 = new Vector3();
            Vector3 lambda3 = new Vector3();
            /* Calculate initial terms */
            p_c = pos - C;                          // sub3(pos3, c, p_c);
            zeta = Vector3.Dot(p_c, O);             // zeta = dot3(p_c, o);
            u3 = zeta * O;                          // scale3(zeta, o, u3);
            lambda3 = p_c - u3;                     // sub3(p_c, u3, lambda3);
            lambda = lambda3.Length();              // lambda = mag3(lambda3);

            /* Calculate theta using Newton's Method */
            n = 0;
            theta = Math.Atan2(lambda, zeta);
            dtheta = 1;
            for (; ; )
            {

                /* Make sure we don't iterate forever */
                if (++n > MAX_NEWTON)
                {
                    throw new CameraModelException("cahvore_3d_to_2d(): too many iterations");
                    //Console.WriteLine("cahvore_3d_to_2d(): too many iterations\n");
                    //break;
                    
                }

                /* Compute terms from the current value of theta */
                costh = Math.Cos(theta);
                sinth = Math.Sin(theta);
                theta2 = theta * theta;
                theta3 = theta * theta2;
                theta4 = theta * theta3;
                upsilon = zeta * costh + lambda * sinth
                - (1 - costh) * (E.X + E.Y * theta2 + E.Z * theta4)         //-(1 - costh) * (e[0] + e[1] * theta2 + e[2] * theta4)
                - (theta - sinth) * (2 * E.Y * theta + 4 * E.Z * theta3);   //-(theta - sinth) * (2 * e[1] * theta + 4 * e[2] * theta3);
                                
                /* Check exit criterion from last update */
                if (Math.Abs(dtheta) < 1e-8)
                    break;

                /* Update theta */
                dtheta = (
                        zeta * sinth - lambda * costh
                        - (theta - sinth) * (E.X + E.Y * theta2 + E.Z * theta4)      //- (theta - sinth) * (e[0] + e[1] * theta2 + e[2] * theta4)
                ) / upsilon;
                theta -= dtheta;
            }

            /* Check the value of theta */
            if ((theta * Math.Abs(Linearity)) > Math.PI / 2.0)
            {
                throw new CameraModelException("cahvore_3d_to_2d(): theta out of bounds");
                //Console.WriteLine("cahvore_3d_to_2d(): theta out of bounds\n");
            }
            Vector3 rp = new Vector3();
            /* Approximations for small theta */
            if (theta < 1e-8)
            {
                rp = p_c;           // copy3(p_c, rp);
            }
            else
            {
                double linth, chi, chi2, chi3, chi4, zetap, mu;

                linth = Linearity * theta;
                if (Linearity < -EPSILON)
                    chi = Math.Sin(linth) / Linearity;
                else if (Linearity > EPSILON)
                    chi = Math.Tan(linth) / Linearity;
                else
                    chi = theta;

                chi2 = chi * chi;
                chi3 = chi * chi2;
                chi4 = chi * chi3;

                zetap = lambda / chi;

                mu = R.X + R.Y * chi2 + R.Z * chi4;      // mu = r[0] + r[1] * chi2 + r[2] * chi4;

                Vector3 v3 = new Vector3();
                u3 = zetap * O;                          // scale3(zetap, o, u3);
                v3 = (1 + mu) * lambda3;                 // scale3(1 + mu, lambda3, v3);
                rp = u3 + v3;                            // add3(u3, v3, rp);
            }

            /* Calculate the projection */
            alpha = Vector3.Dot(rp, A);                 // alpha = dot3(rp, a);
            beta = Vector3.Dot(rp, H);                  // beta = dot3(rp, h);
            gamma = Vector3.Dot(rp, V);                 // gamma = dot3(rp, v);
            if (alpha < EPSILON)
            {
                throw new CameraModelException("divide by zero");      // Don't know if this is possible but why not
            }
            Vector2 pixelPos = new Vector2();
            pixelPos.X = beta / alpha;                  // pos2[0] = beta / alpha;
            pixelPos.Y = gamma / alpha;                 // pos2[1] = gamma / alpha;
            range = p_c.Length() * Math.Sign(alpha);
            return pixelPos;
        }

        public override object Clone()
        {
            return new CAHVORE(this);
        }
    }
}
