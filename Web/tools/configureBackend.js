const fs = require('fs-extra');
const path = require('path');
const config = require('../config');
const { spawnSync, hasFlag } = require('./toolUtil');

//npm run configure-backend -- [venue] [--persist]

const argv = process.argv;
let venue = config.app.venueName;
if (argv.length > 2 && !argv[2].startsWith('-')) venue = argv[2];

const cfgFile = 'ec2userdata.txt';

const args = [
  `--venue=${venue}`,
  `--s3url=${config.app.s3Url}`,
  `--awsregion=${config.app.awsRegion}`,
  `--awsprofile=${config.app.awsProfile}`,
  `--msliceawsprofile=${config.app.MSLICEAWSProfile}`,
  `--mslices3url=${config.app.MSLICES3Url}`,
  `--workerexecutable=${config.workerExe}`,
];

if (!hasFlag('persist')) args.push('--nopersist');

spawnSync(config.exe, ['configure-cloud', ...args], { cwd: config.app.binDir });

const src = path.join(config.app.binDir, cfgFile);
const dst = cfgFile;
fs.copySync(src, dst);
console.log(`copied '${src}' to '${dst}'`);
