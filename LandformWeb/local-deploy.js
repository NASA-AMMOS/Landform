const fs = require('fs-extra');
const path = require('path');
const spawnSync = require('child_process').spawnSync;
const spawn = require('child_process').spawn;
const Zip = require('@jpl/adm-zip');

const { checkDeploy } = require('./deployUtil');

const zf = 'landformweb.zip';

checkDeploy()
  .then((args) => {

    let nextTmpDir = 0, tmpDir = null;
    do { tmpDir = path.join('tmp', `local-deploy${nextTmpDir++}`); } while (fs.pathExistsSync(tmpDir));
    fs.ensureDirSync(tmpDir);

    try {
      const zip = new Zip(zf);

      console.log(`extracting ${zf} to ${tmpDir}`);
      zip.extractAllTo(tmpDir);

      const imageTag = 'landformweb';
      const buildArgs = ['build', '--tag', imageTag, '--build-arg', 'NODE_ENV=production', '.'];
      console.log(`running docker ${buildArgs.join(' ')}`);
      spawnSync('docker', buildArgs, { stdio: 'inherit', cwd: tmpDir, shell: true });

      const envArgs = ['--env', 'LOG_LEVEL=silly'];
      const interactive = args.some(a => a === '-i' || a === '--interactive' || a === '--interactive=true');
      if (interactive) console.log('NOTE: for git bash run \'winpty node local-deploy.js [-f] -i\'');
      const runArgs = interactive ? ['run', ...envArgs, '-it', imageTag, '/bin/bash'] : ['run', imageTag];
      console.log(`running docker ${runArgs.join(' ')}`);
      spawn('docker', runArgs, { stdio: 'inherit', shell: true });

      //the double slash in //bin/bash prevents git bash from munging that path
      //winpty docker run --it landformweb //bin/bash

    } finally { fs.remove(tmpDir); }
  })
  .catch(e => console.error(e.message));
