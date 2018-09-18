# LandformWeb

LandformWeb is a [node.js](https://nodejs.org) server and [React](https://reactjs.org) browser client for controlling the Landform cluster including but not limited to functionality of the Geometry Tiling Server.

Deployed at https://landform.hi.jpl.nasa.gov.  This site is only accessible to JPL IP addresses.  For VPN access, use full tunnel mode.

This repo consists of a backend REST API server and a frontend react app in `/client`.  In production the frontend app is pre-built with webpack and served as static files by the backend server.  During development a separate frontend server is used with hot module reloading.  The react app configuration is managed with [create-react-app](https://github.com/facebook/create-react-app).

Additional docs:
* [REST API](API.md)
* [Test Procedures](TEST.md)
* [AWS Setup](SETUP.md)

## AWS Credentials
Most development and deployment tasks require AWS credentials.
1. Make sure you are in the `aws.589270964471.account_owner` LDAP group.  If not, talk to Alex.
1. Follow the instructions in the header comments of `../Cloud/aws-login.py` to configure your machine with the required Python libraries.  Also follow the optional instructions to install the AWS CLI and AWS Elastic Beanstalk CLI tools.
1. Run `../Cloud/aws-login.py` from the windows `cmd` prompt or `winpty python ../Cloud/aws-login.py` if using git bash (Cygwin bash does not work unfortunately).
    1. Enter your JPL username and password.
    1. If you have multiple roles, select `arn:aws:iam::589270964471:role/account_owner`.

This will generate temporary AWS credentials in `$HOME/.aws/credentials`.  Note credentials generated this way are always temporary and will expire in some hours.  To renew them, run the `aws-login.py` script again.

### Issues & Workarounds
* If you have specialized your `HOME` environment variable to be different from `USERPROFILE` then you will need to create a symlink `USERPROFILE/.aws` -> `HOME/.aws`.  Unfortunately on Windows NPM brutally replaces any custom setting for `HOME` with the value of `USERPROFILE`, which breaks resolution of AWS credentials for commands run as NPM scripts (e.g. `npm start`, `npm run deploy`).  To create a symlink on Windows first enable developer mode in Windows settings.  Then use [mklink](https://blogs.windows.com/buildingapps/2016/12/02/symlinks-windows-10) to create the link.  For example, in Windows `cmd` prompt: `mklink /d %USERPROFILE%\.aws %HOME%\.aws`.

## TilingServer
The backend node.js server will run the .NET tiling server (`../TilingServer` subproject) as a subprocess.  Thus, most development and deployment tasks require that you first use VisualStudio to build the TilingServer subproject.

During local development (environment variable `NODE_ENV=development`) the tiling server binary will be found at `../TilingServer/bin/Release/TilingServer.exe`.  To use the debug build instead specify the `-d` or `--debug` option on the command line, e.g. `npm start -- -d`.

In a production context (`NODE_ENV=production`) the tiling server binary will be found at `./bin/TilingServer.exe`, which will be copied from `../TilingServer/bin/Release/TilingServer.exe` when the build zip is bundled.

### Tiling Worker
In order to run projects you will also need at least one tiling worker connected to the same AWS venue.  For development one option is to run the tiling worker locally:
1. Acquire AWS credentials as described above if you haven't already.
1. `cd ../TilingServer/bin/Release`
1. `TilingServer.exe configure`
    1. enter same venue name, s3 URL, and AWS region as in `config.js`
    1. enter profile name for AWS credentials
1. `TilingServer.exe startworker`

For production TODO.

---

## Development Workflow
1. Install latest [node.js](https://nodejs.org) 8.x.x.
1. Acquire AWS credentials and build `TilingServer.exe` with Visual Studio as explained above.
1. `npm install` in the `LandformWeb` directory
1. Check `venueName` in `config.js` - the server will connect to live AWS services for that venue.
1. `npm start -- [-d|--debug]` will start both the backend api server on port 8081 and the frontend react dev server on port 3000 (the frontend server will proxy backend routes to the backend server).
    1. You can also run the api server and client servers independently with `npm run server -- [-d|--debug]` and `npm run client`.  This is convenient when doing backend dev so that you can independently restart the backend server.
1. Typically you should not need to restart the frontend server for frontend dev because it uses hot module reloading.  However, if you modify the backend server you will need to restart it.
1. Go to http://localhost:3000 and follow the instructions to login and generate an API token.  Note that SSO login will only work when deployed to the production server URL given above, and LDAP login will only work when the server is within the JPL firewall (i.e. not deployed to AWS for production).

### Issues & Workarounds
* CTRL-C may not work correctly to kill the backend server if you started it from a cygwin prompt; consider using a Git bash prompt or Windows `cmd` instead.

## Test & Production Deployment Workflow
1. Install latest [node.js](https://nodejs.org) 8.x.x.
1. Acquire AWS credentials and build `TilingServer.exe` with Visual Studio as explained above.
1. `npm run build` to generate `landformweb.zip`.  This is sugar for `npm install && npm run build-client && npm run bundle`
    * `npm install` - installs `node_modules` and `client/node_modules`
    * `npm run build-client` - runs `npm run build` to webpack `client/build`
    * `npm run bundle` - creates `landformweb.zip` containing
        * full archive of current git HEAD for the `LandformWeb` subtree
        * current `client/build` subtree
        * required binaries from `../TilingServer/bin/Release` under `bin`
1. Optional - test the Docker container locally.
    1. This will require a local installation of [Docker](https://www.docker.com) host.
    1. Check `venueName` in `config.js` - the server will connect to live AWS services for that venue.  The `venueName` that will be used is determined by the `NODE_ENV` environment variable in the shell where the `local-deploy` script is run, even though in the container `NODE_ENV=production` always.  Typically this results in `venueName=landformweb-dev`, which is appropriate for testing.
    1. `npm run local-deploy -- [-f|--force] [-i|--interactive] [-d|--debug]` to re-build the Docker container and run it locally.  The name of the docker container is given by the value of `deployEnvironment` from `config.js`, using the value of `NODE_ENV` in the shell where the `local-deploy` script runs.  Typically `deployEnvironment=landformweb-dev`, which is appropriate for testing.
    1. You can access it at http://localhost:8081.
    1. Options:
        1. `--force`: use existing `landformweb.zip` even if it might be outdated
        1. `--interactive`: drop into a shell in the Docker container instead of running the server.  Note: if using git bash run `winpty node local-deploy.js -i ...` instead.
        1. `--debug`: set `LOG_LEVEL=silly` in the Docker container
1. Deploy to Elastic Beanstalk
    1. Check `deployEnvironment` in `config.js` - the server will be deployed to this Elastic Beanstalk environment.  The `deployEnvironment` that will be used is determined by the `NODE_ENV` environment variable in the shell where the `deploy` script is run, even though in the deployment the value of `NODE_ENV` is typically configured in the Elastic Beanstalk environment as `NODE_ENV=production`.
    1. The `venueName` that will be used in the deployment is typically configured in the Elastic Beanstalk environment as the environment variable `TILE_SERVER_VENUE_NAME`.  Typically the venue name is configured to be the same as the environment name, so the production `landformweb` environment uses the `landformweb` venue, and the testing environment `landformweb-dev` uses the `landformweb-dev` venue.
    1. `npm run deploy -- [environment-name] [-f|--force] [--profile=foo]`.  Options:
        1. `environment-name`: Upload to this Elastic Beanstalk environment instead of the default from `config.js`
        1. `--force`: use existing landformweb.zip even if it might be outdated
        1. `--profile=foo`: use AWS credentials profile `foo` instead of `default`
    1. You can watch the Elastic Beanstalk (re-)deployment progress by logging in to the [AWS web console](http://goto.jpl.nasa.gov/awsconsole).
    1. Once the deployment is complete the site will be live at https://landform-dev.hi.jpl.nasa.gov (omit `-dev` for production).  Note, if using VPN full tunnel is required because we restrict access to JPL IP addresses.
