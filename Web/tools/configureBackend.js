const fs = require('fs-extra');
const path = require('path');
const config = require('../config');
const { spawnSync, hasFlag } = require('./toolUtil');

//npm run configure-backend -- [venue-name] [--persist]

const argv = process.argv;
let venue = config.app.venueName;
if (argv.length > 2 && !argv[2].startsWith('-')) venue = argv[2];

const cfgFile = 'ec2userdata.txt';

const args = [
  `--venuename=${venue}`,
  `--s3url=${config.app.s3Url}`,
  `--region=${config.app.awsRegion}`,
  `--profile=${config.app.awsProfile}`,
  `--msliceprofile=${config.app.awsMSLICEProfile}`,
  `--mslices3url=${config.app.awsMSLICES3Url}`,
];

if (!hasFlag('persist')) args.push('--nopersist');

spawnSync('TilingServer.exe', ['configure', ...args], { cwd: config.app.binDir });

const src = path.join(config.app.binDir, cfgFile);
const dst = cfgFile;
fs.copySync(src, dst);
console.log(`copied '${src}' to '${dst}'`);
