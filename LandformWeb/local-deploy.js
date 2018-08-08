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

      runArgs.push('--mount', `type=bind,source="${path.join(process.env.HOME, '.aws')}",target=/root/.aws,readonly`);

      runArgs.push('-p', `${config.app.port}:${config.app.port}`);

      if (args.some(a => boolArg(a, 'interactive'))) {
        console.log('NOTE: for git bash run \'winpty node local-deploy.js [-f] [-d] -i\'');
        runArgs.push('-it', imageName, '/bin/bash');
      } else runArgs.push(imageName);

      console.log(`running docker ${runArgs.join(' ')}`);
      spawn('docker', runArgs, { stdio: 'inherit', shell: true });

      //example interactive command line: docker run --it <image-name> /bin/bash
      //
      //or for git bash: winpty docker run --it <image-name> //bin/bash
      //
      //the double slash in //bin/bash prevents git bash from munging that path

    } finally { fs.remove(tmpDir); }
  })
  .catch(e => console.error(e.message));
