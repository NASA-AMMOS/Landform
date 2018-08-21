const fs = require('fs-extra');
const spawn = require('child_process').spawn;

const cdir = 'client';
if (process.env.NODE_ENV === 'production') console.log(`skipping npm install in ${cdir}/, NODE_ENV=production`);
else if (fs.pathExistsSync(cdir)) {
  console.log(`npm install-ing in ${cdir}/`);
  spawn('npm', ['install'], { stdio: 'inherit', cwd: cdir, shell: true });
} else console.log(`skipping npm install in ${cdir}/, directory does not exist`);
