Dependencies for deployment, including on Windows Server Core.

To check dependencies use dumpbin.exe that comes with visual studio, e.g.

dumpbin /dependents exe-or-dll

https://withinrafael.com/2017/09/09/windows-container-app-compat-opencv-python/

cvextern.dll (OpenCV) depends on
msvfw32.dll
avifil32.dll
avicap32.dll
msacm32.dll
(see vfw-dlls.zip)

embree.dll depends on tbb.dll which depends on VC 2013 redistributable
msvcp120.dll
msvcr120.dll

PoissonRecon.V13.72.exe, SurfaceTrimmer.V13.72.exe, fssrecon.exe and meshclean.exe depend on VC 2015 redistributable
msvcp140.dll
vcomp140.dll
vcruntime140.dll
