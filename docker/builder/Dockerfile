FROM ubuntu:25.04

RUN apt-get update -y && apt-get install -y vim dotnet-sdk-9.0 build-essential cmake unzip curl 

# Emgu.CV dependencies
# https://github.com/emgucv/emgucv/blob/4.12.0/platforms/ubuntu/24.04/apt_install_dependency
RUN apt-get install -y libgtk-3-dev libgstreamer1.0-dev libavcodec-dev libswscale-dev libavformat-dev libdc1394-dev libv4l-dev ocl-icd-dev freeglut3-dev libgeotiff-dev libusb-1.0-0-dev libvtk9-dev libfreetype-dev libharfbuzz-dev qtbase5-dev libeigen3-dev libgstreamer1.0-dev libgstreamer-plugins-base1.0-dev libgflags-dev libgoogle-glog-dev liblapacke-dev libva-dev

RUN groupadd -r app && useradd -r -g app app

USER app

WORKDIR /home/app

RUN dotnet dev-certs https --trust
