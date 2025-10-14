#dotnet-sdk-9.0 is available on Ubuntu 25.04 but not 24.04
#
#unfortunately some of the Emgu.CV dependencies in 25.04 are later versions than Emgu.CV will accept, in particular
#
#libavcodec-dev libswscale-dev libavformat-dev libvtk9-dev 
#
#so use Ubuntu 24.04 but install dotnet-sdk-9.0 from ppa:dotnet/backports
#FROM ubuntu:25.04
FROM ubuntu:24.04

RUN apt-get update -y && apt-get install -y vim build-essential cmake unzip curl git

# UVAtlas appears to require at least g++-14 to build
RUN apt-get install -y gcc-14 g++-14
RUN update-alternatives --install /usr/bin/gcc gcc /usr/bin/gcc-14 140 --slave /usr/bin/g++ g++ /usr/bin/g++-14

RUN apt-get install -y software-properties-common && add-apt-repository ppa:dotnet/backports && apt-get update -y && apt-get install -y dotnet-sdk-9.0 

# Emgu.CV dependencies
# https://github.com/emgucv/emgucv/blob/4.12.0/platforms/ubuntu/24.04/apt_install_dependency
RUN apt-get install -y libgtk-3-dev libgstreamer1.0-dev libavcodec-dev libswscale-dev libavformat-dev libdc1394-dev libv4l-dev ocl-icd-dev freeglut3-dev libgeotiff-dev libusb-1.0-0-dev libvtk9-dev libfreetype-dev libharfbuzz-dev qtbase5-dev libeigen3-dev libgstreamer1.0-dev libgstreamer-plugins-base1.0-dev libgflags-dev libgoogle-glog-dev liblapacke-dev libva-dev

RUN echo "root:root" | chpasswd

RUN groupadd -r app && useradd -r -g app app

USER app

WORKDIR /home/app

RUN dotnet dev-certs https --trust
