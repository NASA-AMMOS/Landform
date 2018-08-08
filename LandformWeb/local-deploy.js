const fs = require('fs-extra');
const path = require('path');
const spawnSync = require('child_process').spawnSync;
const spawn = require('child_process').spawn;
const Zip = require('@jpl/adm-zip');

const config = require('./config');
const { boolArg, checkDeploy } = require('./deployUtil');

//npm run local-deploy -- [-f|--force|--force=true] [-i|--interactive|--interactive=true] [-d|--debug|--debug=true]

checkDeploy()
  .then((args) => {

    let nextTmpDir = 0, tmpDir = null;
    do { tmpDir = path.join('tmp', `local-deploy${nextTmpDir++}`); } while (fs.pathExistsSync(tmpDir));
    fs.ensureDirSync(tmpDir);

    try {
      const zip = new Zip(config.app.bundle);

      console.log(`extracting ${config.app.bundle} to ${tmpDir}`);
      zip.extractAllTo(tmpDir);

      const imageName = config.app.localDeployTag;
      const buildArgs = ['build', '--tag', imageName, '--build-arg', 'NODE_ENV=production', '.'];
      console.log(`running docker ${buildArgs.join(' ')}`);
      spawnSync('docker', buildArgs, { stdio: 'inherit', cwd: tmpDir, shell: true });

      const runArgs = ['run'];

      if (args.some(a => boolArg(a, 'debug'))) runArgs.push('--env', 'LOG_LEVEL=silly');

      //process.env.HOME lies on windows when run as an npm script and HOME != USERPROFILE
      //https://github.com/nodejs/node/issues/13818
      //so e.g. if HOME=C:\cygwin64\home\vona and USERPROFILE=C:\Users\vona
      //then here we will get process.env.HOME=C:\Users\vona
      //so workaround that by symlinking C:\Users\vona\.aws -> C:\cygwint64\home\vona\.aws
      //but then resolve that symlink before embedding it in the --mount argument for docker
      //because docker apparrently can't do that on its own
      const awsDir = fs.realpathSync(path.join(process.env.HOME, '.aws'));
      runArgs.push('--mount', `type=bind,source="${awsDir}",target=/root/.aws,readonly`);
      runArgs.push('--env', `AWS_PROFILE=${config.app.awsProfile}`);
      runArgs.push('--env', 'WITHOUT_HTTPS=true');

      runArgs.push('-p', `${config.app.port}:${config.app.port}`);

      if (args.some(a => boolArg(a, 'interactive'))) {
        console.log('NOTE: for git bash run \'winpty node local-deploy.js [-f] [-d] -i\'');
        runArgs.push('-it', imageName, '/bin/bash');
      } else runArgs.push(imageName);

      console.log(`running docker ${runArgs.join(' ')}`);
      spawn('docker', runArgs, { stdio: 'inherit', shell: true });

      //example interactive command line: docker run -it <image-name> /bin/bash
      //or for git bash: winpty docker run -it <image-name> //bin/bash
      //the double slash in //bin/bash prevents git bash from munging that path

    } finally { fs.remove(tmpDir); }
  })
  .catch(e => console.error(e.message));
