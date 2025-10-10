#dotnet-runtime-9.0 is available on Ubuntu 25.04 but not 24.04
#
#unfortunately some of the Emgu.CV dependencies in 25.04 are later versions than Emgu.CV will accept, in particular
#
#libavcodec-dev libswscale-dev libavformat-dev libvtk9-dev 
#
#so use Ubuntu 24.04 but install dotnet-runtime-9.0 from ppa:dotnet/backports
#FROM ubuntu:25.04
FROM ubuntu:24.04

RUN apt-get update -y && apt-get install -y vim

RUN apt-get install -y software-properties-common && add-apt-repository ppa:dotnet/backports && apt-get update -y && apt-get install -y dotnet-runtime-9.0 

# Emgu.CV dependencies
# https://github.com/emgucv/emgucv/blob/4.12.0/platforms/ubuntu/24.04/apt_install_dependency
RUN apt-get install -y build-essential libgtk-3-dev libgstreamer1.0-dev libavcodec-dev libswscale-dev libavformat-dev libdc1394-dev libv4l-dev ocl-icd-dev freeglut3-dev libgeotiff-dev libusb-1.0-0-dev libvtk9-dev libfreetype-dev libharfbuzz-dev qtbase5-dev libeigen3-dev libgstreamer1.0-dev libgstreamer-plugins-base1.0-dev libgflags-dev libgoogle-glog-dev liblapacke-dev libva-dev

RUN groupadd -r app && useradd -r -g app app

USER app

WORKDIR /home/app
