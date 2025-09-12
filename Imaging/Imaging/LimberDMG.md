# Composite Image Stitching in LimberDMG

The core idea of composite image stitching is to reduce the visibility of seams in an image that is composed of multiple sub-images.

This is based on work originally published by Misha Kazhdan [1], who provides a [reference implementation](http://www.cs.jhu.edu/~misha/Code/DMG) ([github](https://github.com/mkazhdan/DMG)) which he refers to as "DMG".  DMG stands for "Distributed Multigrid", and Misha's work can actually perform a variety of imaging operations in a distributed multigrid framework; not just composite image stitching but also smoothing, sharpening, and "high-low compositing".  The algorithm and implementation can process large (gigapixel) images by splitting the original image into bands which are solved in parallel on multiple CPUs.  The problem is not trivially parallelizable, so this involves carefully scheduled communication between the CPUs.

The DMG framework solves the [screened Poisson equation](https://en.wikipedia.org/wiki/Screened_Poisson_equation) to find an image that best fits given constraining (a) value and (b) gradient images.  The composite image stitching problem is mapped to this framework by setting the gradient constraint to 0 at the image seams to be smoothed.

DMG appears to be a successor to "Streaming Multigrid" [SMG](https://www.cs.jhu.edu/~misha/Code/SMG) [2].

In Landform we are really interested specifically in composite image stitching, but because we originally implemented it by calling out to Misha's DMG.exe, we now colloquially refer to composite image stitching as "DMG".

Around November 2014, Charley Goddard re-implemented the screened Poisson formulation of composite image stitching in one C# file as LimberDMG.cs.  "Limber" (presumably with the meaning "lithe or supple") is simply a word that Charley picked, and "DMG" is really a misnomer here, because this implementation is not distributed (though it is multigrid).  It operates on simple monolithic in-core images, though it is parallelized in a few places.  It has some sparse in-line documentation.

Charley wrote:
> I took a lot of inspiration from Misha's DMG and SMG but diverged a bit, both for ease of implementation and for additional functionality. The b-spline basis functions (among other things) got thrown out in favor of simpler discretized derivatives. I think I more-or-less took the core idea of solving the screened poisson equation, framed it as an affine transformation to allow pixels to be held constant, and worked out the math on paper.  Section 3 of [3] has a similar derivation to how I probably got that particular equation.  Note that it looks like they used a different sign convention. (or maybe I goofed?)

Around June 2018, Andrew Zhang added LimberDMG.cs to the Landform repository using the Landform Image class.

### References

[1] [Kazhdan, Surendran, Hoppe.  Distributed Gradient-Domain Processing of Planar and Spherical Images.  ACM Transactions On Graphics, Vol 29, No 2, April 2010.](http://www.cs.jhu.edu/~misha/MyPapers/ToG10.pdf)

[2] [Kazhda, Hoppe. Streaming Multigrid for Gradient-Domain Operations on Large Images. SIGGRAPH 2008.](http://www.cs.jhu.edu/~misha/MyPapers/SIG08.pdf)

[3] [Bhat, Curless, Cohen, Zitnick. Fourier Analysis of the 2D Screened Poisson Equation for Gradient Domain Problems.  ECCV 2008](https://grail.cs.washington.edu/projects/screenedPoissonEq/screenedPoissonEq_files/screenedPoissonEq.pdf)

