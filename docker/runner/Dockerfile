FROM amazonlinux:2023

RUN dnf update -y

RUN dnf install -y vim zip unzip shadow-utils && ln -s /usr/bin/vim /usr/bin/vi

RUN dnf install -y libomp libpng libtiff libgeotiff libjpeg-turbo mesa-libGL mesa-libGLU freeglut
 
RUN dnf install -y dotnet-runtime-9.0

RUN dnf clean all && rm -rf /var/cache/dnf

RUN cd /tmp && curl https://awscli.amazonaws.com/awscli-exe-linux-x86_64.zip -o awscli.zip && unzip awscli.zip && ./aws/install && rm awscli.zip && rm -rf aws

RUN groupadd --gid 1001 appgrp && useradd --uid 1001 --gid appgrp -m app

USER app

WORKDIR /home/app
