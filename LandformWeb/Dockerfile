FROM node:8

RUN apt-get update
RUN apt-get install -y mono-runtime libmono-system-core4.0-cil libmono-system-xml-linq4.0-cil libmono-system-runtime-serialization4.0-cil libmono-system-numerics4.0-cil libmono-system-data4.0-cil

ARG NODE_ENV
ENV NODE_ENV $NODE_ENV

RUN mkdir -p /usr/src/app
WORKDIR /usr/src/app

COPY . .

RUN npm install --production

EXPOSE 8081

CMD ["node", "server.js"]
