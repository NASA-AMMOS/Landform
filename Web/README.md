# LandformWeb
LandformWeb is a [node.js](https://nodejs.org) server and [React](https://reactjs.org) browser client for controlling the Landform cluster including but not limited to functionality of the Geometry Tiling Server.

The frontend React app (in the `client` subdir) is managed with [create-react-app](https://github.com/facebook/create-react-app).  In production the frontend app is pre-built with webpack and served as static files by the backend server, which also serves the REST API.  During development a separate frontend server is used with webpack hot module reloading for the frontend and a proxy setup to relay REST APIs to the backend.

Additional docs:
* [REST API](docs/API.md)
* [Test Procedures](docs/TEST.md)
* [AWS Setup](docs/SETUP.md)
* [Landform Internal](docs/INTERNAL.md)

### Issues & Workarounds
* If you have specialized your `HOME` environment variable to be different from `USERPROFILE` then you will need to create a symlink `USERPROFILE/.aws` -> `HOME/.aws`.  Unfortunately on Windows NPM brutally replaces any custom setting for `HOME` with the value of `USERPROFILE`, which breaks resolution of AWS credentials for commands run as NPM scripts (e.g. `npm start`, `npm run deploy`).  To create a symlink on Windows first enable developer mode in Windows settings.  Then use [mklink](https://blogs.windows.com/buildingapps/2016/12/02/symlinks-windows-10) to create the link.  For example, in Windows `cmd` prompt: `mklink /d %USERPROFILE%\.aws %HOME%\.aws`.

## TilingServer
The node.js web server will run the .NET tiling server (`../TilingServer`) as a subprocess.  Thus, most development and deployment tasks require that you first use VisualStudio to build the `TilingServer` subproject.

During local development (environment variable `NODE_ENV=development`) the tiling server binary will be found at `../TilingServer/bin/Release/TilingServer.exe`.  To use the debug build of `TilingServer.exe` instead of the release version, set the environment variable `DEBUG_TILING_SERVER=true`.

In a deployed context (`NODE_ENV=production` or `NODE_ENV=integration`) the tiling server binary will be found at `./bin/TilingServer.exe`, which will be copied from `../TilingServer/bin/Release/TilingServer.exe` when the build zip is bundled.

## Tiling Worker
In order to run projects you will also need at least one running tiling worker connected to the same AWS venue.  For development one option is to run the tiling worker locally:
1. `npm run show-config` - the server will connect to live AWS services for that venue
2. `npm run worker` or `npm run worker -- venue-name` to use a different venue.

For production or integration testing the worker is [deployed to an EC2 autoscale group](#deploy-worker-to-ec2).

### Issues & Workarounds
* CTRL-C may not work correctly to kill the worker if you started it from a cygwin prompt; consider using a Git bash prompt or Windows `cmd` instead.

---

## Development Workflow
First install latest [node.js](https://nodejs.org), [acquire AWS credentials and set environment vars](docs/INTERNAL.md#aws-credentials), and build `TilingServer.exe` with Visual Studio as explained [above](#tilingserver).

1. `npm install`
1. `npm run show-config` - the server will connect to live AWS services for the displayed venue.
1. Make sure a [tiling worker](#tiling-worker) is running in that venue.
1. `npm start` will start both the backend api server on port 8081 and the frontend react dev server on port 3000 (the frontend server will proxy backend routes to the backend server).
    1. You can also run the api server and client servers independently with `npm run server` and `npm run client`.  This is convenient when doing backend dev so that you can independently restart the backend server.
1. Typically you should not need to restart the frontend server for frontend dev because it uses hot module reloading.  However, if you modify the backend server you will need to restart it.
1. Go to http://localhost:3000 and follow the instructions to login and generate an API token.  Note that SSO login will only work when deployed to the production server URL given above, and LDAP login will only work when the server is within the JPL firewall (i.e. not deployed to AWS for production).

### Issues & Workarounds
* CTRL-C may not work correctly to kill the backend server if you started it from a cygwin prompt; consider using a Git bash prompt or Windows `cmd` instead.
* if using git bash some commands may need to be prefixed with `winpty`

## Test & Deployment Workflow
First install latest [node.js](https://nodejs.org), [acquire AWS credentials and set environment vars](docs/INTERNAL.md#aws-credentials), and build `TilingServer.exe` with Visual Studio as explained above.

### 1. Generate Release Bundle
Run `npm run build` to generate `landform-master.zip`.

This is sugar for `npm install && npm run build-client && npm run bundle`.
* `npm install` installs `node_modules` and `client/node_modules`
* `npm run build-client` runs `npm run build` to webpack `client/build`
* `npm run bundle` creates `landform-master.zip` containing
    * full archive of current git HEAD for the `Web` subtree
    * current `client/build` subtree
    * required binaries from `../TilingServer/bin/Release` under `bin`

### 2. Test the Master Server in a Docker Container Locally (Optional)
This will require a local installation of [Docker](https://www.docker.com) host.

1. `npm run show-config` - the server will connect to live AWS services for that venue.
   1. The `venueName` that will be used is determined by the `NODE_ENV` environment variable in the shell where the `local-deploy` script is run, even though in the container `NODE_ENV=production` always.  This enables testing a local deployment connected to a private AWS venue.
1. `npm run local-deploy -- [-f|--force] [-i|--interactive] [-d|--debug]` to re-build the Docker container and run it locally.  The name of the docker container is given by the value of `deployEnvironment` from `config.js`, using the value of `NODE_ENV` in the shell where the `local-deploy` script runs.  Typically `deployEnvironment=landformweb-dev`, which is appropriate for testing.
1. Options:
    * `--force`: use existing `landform-master.zip` even if it might be outdated
    * `--interactive`: drop into a shell in the Docker container instead of running the server.  Note: if using git bash run `winpty node tools/localDeploy.js -i ...` instead.
    * `--debug`: set `LOG_LEVEL=silly` in the Docker container
1. You can now access the server at http://localhost:8081.
1. Make sure a [tiling worker](#tiling-worker) is running in that venue, then run through the [test procedures](docs/TEST.md).
1. To kill the docker container, run `docker ps` to get its name and then run `docker kill NAME`.

### 3. Deploy Master Server to Elastic Beanstalk
It is also possible to manually deploy the release bundle using the AWS Elastic Beanstalk web console, as documented in the [AWS setup](docs/SETUP.md) instructions.

1. Check `deployEnvironment` in `config.js` - the server will be deployed to this Elastic Beanstalk environment unless a different environment is explicitly named on the command line as explained below.  The `deployEnvironment` that will be used is determined by the `NODE_ENV` environment variable in the shell where the `deploy` script is run.
1. The `venueName` that will be used in the deployment typically depends on the `NODE_ENV` configured in the Elastic Beanstalk environment.  Typically the environment `landformweb-dev` has `NODE_ENV=integration` and the environment `landformweb` has `NODE_ENV=production`.  It is also possible to override `LANDFORM_VENUE` directly in the environment configuration.
1. `npm run deploy -- [environment-name] [-f|--force] [--profile=foo]`.
1. Options:
    * `environment-name`: Upload to this Elastic Beanstalk environment instead of the default from `config.js`
    * `--force`: use existing landform-master.zip even if it might be outdated
    * `--profile=foo`: use AWS credentials profile `foo` instead of `default`
1. The deployment process will take a few minutes. You can watch the deployment progress by logging in to the AWS web console.
1. Once the deployment is complete the site will be live at https://landformweb-dev.$LANDFORM_AWS_REGION.elasticbeanstalk.com (omit `-dev` for production).  Note, if using VPN full tunnel is required because we restrict access to JPL IP addresses.
1. Make sure a [tiling worker](#tiling-worker) is running in the venue.
1. Run through the [test procedures](docs/TEST.md).

### Deploy Worker to EC2
First install latest [node.js](https://nodejs.org), [acquire AWS credentials and set environment vars](docs/INTERNAL.md#aws-credentials), and build `TilingServer.exe` with Visual Studio as explained [above](#tilingserver).

1. `npm run configure-backend -- [venue-name]` - this will generate a customized `ec2userdata.txt`.  This file will be used to configure instances in an EC2 autoscale group.
1. `npm run bundle-worker` - this will generate `landform-worker.zip` containing the binaries the instances in the autoscale group will run.
1. Deploy the worker using the AWS EC2 web console as documented in the [AWS setup](docs/SETUP.md) instructions.

### Running the Test Projects Locally
Runs the tests defined in [test/data/landform-test-config.json](test/data/landform-test-config.json) using the [tools/runTests.js](toosl/runTests.js) script

1. Download and unzip https://landlords-dev.s3.amazonaws.com/landformweb-test-data/landform-test-data.zip
2. The venue to be tested is defined by `serverUrl` in `landform-test-data/landform-test-config.json`.  It defaults to `http://localhost:8081`, which is for a local venue. Start up the landform master and worker locally following the [development workflow](#development-workflow) above.
3. Run `npm run tests /path/to/landform-test-data`.

TLDR:

    # install node.js

    # in terminal 1
    cd Landform/Web
    npm install
    npm run server

    # in terminal 2
    cd Landform/Web
    npm run worker

    # in terminal 3
    cd Landform/Web
    curl -O https://landlords-dev.s3.amazonaws.com/landformweb-test-data/landform-test-data.zip
    unzip landform-test-data.zip
    npm run tests landform-test-data

