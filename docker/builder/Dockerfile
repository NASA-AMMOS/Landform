FROM amazonlinux:2023

RUN dnf update -y

RUN dnf install -y vim cmake make unzip zip git patch && ln -s /usr/bin/vim /usr/bin/vi

# UVAtlas appears to require at least g++-14 to build
RUN dnf install -y gcc14 gcc14-c++ && ln -s /usr/bin/gcc14-gcc /usr/bin/gcc && ln -s /usr/bin/gcc14-g++ /usr/bin/g++

RUN dnf install -y libpng-devel libtiff-devel libgeotiff-devel libjpeg-turbo-devel mesa-libGL-devel mesa-libGLU-devel freeglut-devel
 
RUN dnf install -y dotnet-sdk-9.0

RUN dnf clean all && rm -rf /var/cache/dnf

RUN cd /tmp && curl https://awscli.amazonaws.com/awscli-exe-linux-x86_64.zip -o awscli.zip && unzip awscli.zip && ./aws/install && rm awscli.zip && rm -rf aws

RUN echo "root:root" | chpasswd

RUN groupadd --gid 1001 appgrp && useradd --uid 1001 --gid appgrp -m app

USER app

WORKDIR /home/app

# attempt to silence first-run spew; run this now that we're user "app"
RUN dotnet dev-certs https --trust || true

