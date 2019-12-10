replacement ivcat.exe that works on an EC2 instance

addresses https://github.jpl.nasa.gov/OnSight/Landform/issues/816

this is a stopgap solution

#816 seems to be due to lack of opengl32.dll on the EC2 instance

working around that by using a software only opengl32.dll from 

https://download.blender.org/ftp/sergey/softwaregl/win64/opengl32.dll

in addition, the version of Coin3D and ivcat.exe here were compiled from source by vona

build instructions https://bitbucket.org/Coin3D/coin/wiki/BuildWithCMake

hg repository https://bitbucket.org/Coin3D/coin

c:\cygwin64\home\vona\src\others\Coin3D\coin>hg parent
changeset:   12099:66d5725bd529
tag:         tip
parent:      12090:d15e1ce96b5f
parent:      12098:4f6784757307
user:        Volker Enderlein <volkerenderlein@hotmail.com>
date:        Wed Dec 04 21:22:45 2019 +0000
summary:     Merged in VolkerEnderlein/coin/TesselationIssues (pull request #177)

https://sourceforge.net/code-snapshots/svn/i/in/inventor-tools/code/inventor-tools-code-r194-trunk.zip

deployment:

ivcat.exe, Coin4.dll, and opengl32.dll were manually copied to c:\landform\Landform-1.7.0\ExternalApps on the EC2 instance

c:\landform\Landform-1.7.0\process-tactical-m20-ec2.bat was modified to specify --meshformat iv instead of obj

note that tiling will still be triggered by upload of obj not iv, but will use the iv (because we have agreed with Galen that the OBJ will always be uploaded last)
