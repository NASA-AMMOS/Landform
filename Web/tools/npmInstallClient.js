const fs = require('fs-extra');
const spawn = require('child_process').spawn;

const dir = 'client';
if (process.env.NODE_ENV === 'production') console.log(`skipping npm install in ${dir}/, NODE_ENV=production`);
else if (fs.pathExistsSync(dir)) {
  console.log(`npm install-ing in ${dir}/`);
  spawn('npm', ['install'], { stdio: 'inherit', cwd: dir, shell: true });
} else console.log(`skipping npm install in ${dir}/, directory does not exist`);
