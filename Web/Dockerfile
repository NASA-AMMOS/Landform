FROM node:8

#use newer mono than the default for debian jesse (node:8 is based on debian jessie)
#because among other things the default mono doesn't trust any https certs by default
#and the ways to get it to trust the normal root certs are now stale
#https://www.mono-project.com/download/stable/#download-lin-debian
RUN apt-key adv --keyserver hkp://keyserver.ubuntu.com:80 --recv-keys 3FA7E0328081BFF6A14DA29AA6A19B38D3D831EF
RUN apt-get update
RUN apt-get install apt-transport-https
RUN echo "deb https://download.mono-project.com/repo/debian stable-jessie main" | tee /etc/apt/sources.list.d/mono-official-stable.list
RUN apt-get update
RUN apt-get install -y mono-runtime libmono-system-core4.0-cil libmono-system-xml-linq4.0-cil libmono-system-runtime-serialization4.0-cil libmono-system-numerics4.0-cil libmono-system-data4.0-cil ca-certificates-mono

#in a local deployment the localDeploy.js script will populate ./.aws with the contents of $HOME/.aws
#in a deployment to Elastic Beanstalk ./.aws will be effectively empty (but it will exist)
RUN mkdir -p /root/.aws
COPY ./.aws /root/.aws

ARG NODE_ENV
ENV NODE_ENV $NODE_ENV

RUN mkdir -p /usr/src/app
WORKDIR /usr/src/app

COPY . .

#--unsafe-perm makes npm run as 'root' instead of 'nobody' which avoids
#npm WARN lifecycle landformweb@1.0.0~postinstall: cannot run in wd ... node tools/npmInstallClient.js /usr/src/app
RUN npm install --production --unsafe-perm

EXPOSE 8081

CMD ["node", "server.js"]
