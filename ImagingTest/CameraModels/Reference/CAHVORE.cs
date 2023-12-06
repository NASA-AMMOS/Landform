using System;

namespace ImagingTest.Reference
{
    public class CAHVORE : CAHVOR
    {
        const int MAX_NEWTON = 100;
        const int CMOD_CAHVORE_TYPE_PERSPECTIVE = 1;
        const int CMOD_CAHVORE_TYPE_FISHEYE = 2;
        const int CMOD_CAHVORE_TYPE_GENERAL = 3;
        protected double[] e;
        protected int mtype;
        protected double mparm;
        protected double linearity = -1;

        public CAHVORE(double[] c, double[] a, double[] h, double[] v, double[] o, double[] r, double[] e, double mparm, int mtype): base (c, a, h, v, o, r) 
        {
           InitParms(e, mtype, mparm);
        }

        private void InitParms(double[] e, int mtype, double mparm) {
            this.e = e;
            this.mparm = mparm;
            this.mtype = mtype;

            this.linearity = -1;

            switch (mtype) {
	        case CMOD_CAHVORE_TYPE_PERSPECTIVE:	/* perspective projection */
	            linearity = 1;
	            break;
	        case CMOD_CAHVORE_TYPE_FISHEYE:		/* fisheye */
	            linearity = 0;
	            break;
	        case CMOD_CAHVORE_TYPE_GENERAL:		/* parametric */
	            linearity = mparm;
	            break;
	        default:
	            Console.WriteLine("Unexpected CAHVORE model type: " + mtype);
                break;
	        }
        }

        public double[] E { get { return e; } }
        public double MParm { get { return mparm; } }
        public double MType { get { return mtype; } } 

        public override void Project_3d_to_2d(double[] pos3,	/* input 3D position */
            out double range,	/* output range along A (same units as C). */
            double[] pos2)
        {
            cmod_cahvore_3d_to_2d(pos3, out range, pos2);
        }

        public override void Project_2d_to_3d(double[] pos2, double[] pos3, double[] uvec3)
        {
            cmod_cahvore_2d_to_3d(pos2, pos3, uvec3);
        }

        public void cmod_cahvore_2d_to_3d(double[] pos2, double[] pos3, double[] uvec3) {
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

            double[] u3 = new double[3];
            double[] v3 = new double[3];
            double[] w3 = new double[3];
            double[] rp = new double[3];
            double[] lambdap3 = new double[3];

		    scale3(pos2[1], a, u3);
		    sub3(v, u3, u3);
		    scale3(pos2[0], a, v3);
		    sub3(h, v3, v3);
		    cross3(u3, v3, w3);
		    cross3(v, h, u3);
		    avh1 = 1/dot3(a, u3);
		    scale3(avh1, w3, rp);
		
		    zetap = dot3(rp, o);
		
		    scale3(zetap, o, u3);
		    sub3(rp, u3, lambdap3);
		
		    lambdap = mag3(lambdap3);
		
		    chip = lambdap / zetap;

            double[] cp = new double[3];
            double[] ri = new double[3];
		    /* Approximations for small angles */
		    if (chip < 1e-8) {
			    copy3(c, cp);
			    copy3(o, ri);
		    }
		
		    /* Full calculations */
		    else {
			    int n;
			    double dchi, s;
			
			    /* Calculate chi using Newton's Method */
			    n = 0;
			    chi = chip;
			    dchi = 1;
			    for (;;) {
				    double deriv;
				
				    /* Make sure we don't iterate forever */
				    if (++n > MAX_NEWTON) {
					    Console.WriteLine("cahvore_2d_to_3d(): too many iterations\n");
					    break;
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
				    deriv = (1 + r[0]) + 3*r[1]*chi2 + 5*r[2]*chi4;
				    dchi = ((1 + r[0])*chi + r[1]*chi3 + r[2]*chi5 - chip) / deriv;
				    chi -= dchi;
			    }
			
			    /* Compute the incoming ray's angle */
			    linchi = linearity * chi;
			    if (linearity < -EPSILON)
				    theta = Math.Asin(linchi) / linearity;
			    else if (linearity > EPSILON)
				    theta = Math.Atan(linchi) / linearity;
			    else
				    theta = chi;
			
			    theta2 = theta * theta;
			    theta3 = theta * theta2;
			    theta4 = theta * theta3;
			
			    /* Compute the shift of the entrance pupil */
			    s = (theta/Math.Sin(theta) - 1) * (e[0] + e[1]*theta2 + e[2]*theta4);
			
			    /* The position of the entrance pupil */
			    scale3(s, o, cp);
			    add3(c, cp, cp);
			
			    /* The unit vector along the ray */
			    unit3(lambdap3, u3);
			    scale3(Math.Sin(theta), u3, u3);
			    scale3(Math.Cos(theta), o, v3);
			    add3(u3, v3, ri);
		    }
		
            copy3(cp, pos3);
		    copy3(ri, uvec3);
	    }

        public void cmod_cahvore_3d_to_2d(double[] pos3, out double range, double[] pos2) {
		    int n;
		    int linearity = mtype;
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

            double[] p_c = new double[3];
            double[] u3 = new double[3];
            double[] lambda3 = new double[3];
		    /* Calculate initial terms */
		    sub3(pos3, c, p_c);
		    zeta = dot3(p_c, o);
		    scale3(zeta, o, u3);
		    sub3(p_c, u3, lambda3);
		    lambda = mag3(lambda3);
		
		    /* Calculate theta using Newton's Method */
		    n = 0;
		    theta = Math.Atan2(lambda, zeta);
		    dtheta = 1;
		    for (;;) {
			
			    /* Make sure we don't iterate forever */
			    if (++n > MAX_NEWTON) {
				    Console.WriteLine("cahvore_3d_to_2d(): too many iterations\n");
				    break;
			    }
			
			    /* Compute terms from the current value of theta */
			    costh = Math.Cos(theta);
			    sinth = Math.Sin(theta);
			    theta2 = theta * theta;
			    theta3 = theta * theta2;
			    theta4 = theta * theta3;
			    upsilon = zeta*costh + lambda*sinth
			    - (1     - costh) * (e[0] +  e[1]*theta2 +   e[2]*theta4)
			    - (theta - sinth) * (      2*e[1]*theta  + 4*e[2]*theta3);
			
			    /* Check exit criterion from last update */
			    if (Math.Abs(dtheta) < 1e-8)
				    break;
			
			    /* Update theta */
			    dtheta = (
					    zeta*sinth - lambda*costh
					    - (theta - sinth) * (e[0] + e[1]*theta2 + e[2]*theta4)
			    ) / upsilon;
			    theta -= dtheta;
		    }
		
		    /* Check the value of theta */
		    if ((theta * Math.Abs(linearity)) > Math.PI/2.0)
			    Console.WriteLine("cahvore_3d_to_2d(): theta out of bounds\n");

            double[] rp = new double[3];
		    /* Approximations for small theta */
		    if (theta < 1e-8) {
			    copy3(p_c, rp);			
		    } 
            else {
			    double linth, chi, chi2, chi3, chi4, zetap, mu;
			
			    linth = linearity * theta;
			    if (linearity < -EPSILON)
				    chi = Math.Sin(linth) / linearity;
			    else if (linearity > EPSILON)
				    chi = Math.Tan(linth) / linearity;
			    else
				    chi = theta;
			
			    chi2 = chi * chi;
			    chi3 = chi * chi2;
			    chi4 = chi * chi3;
			
			    zetap = lambda / chi;
			
			    mu = r[0] + r[1]*chi2 + r[2]*chi4;

                double[] v3 = new double[3];
			    scale3(zetap, o, u3);
			    scale3(1+mu, lambda3, v3);
			    add3(u3, v3, rp);
			}

			/* Calculate the projection */
			alpha  = dot3(rp, a);
			beta   = dot3(rp, h);
			gamma  = dot3(rp, v);
			pos2[0] = beta  / alpha;
			pos2[1] = gamma / alpha;
    		range = alpha;
        }
    }
}
