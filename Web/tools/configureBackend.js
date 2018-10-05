const fs = require('fs-extra');
const path = require('path');
const config = require('../config');
const { spawnSync } = require('./deployUtil');

//npm run configure-backend -- [venue-name]

let venue = config.app.venueName;
if (process.argv.length > 2) venue = process.argv[2];

const cfgFile = 'ec2userdata.txt';

spawnSync('TilingServer.exe',
          ['configure',
            `--venuename=${venue}`,
            `--s3url=${config.app.s3Url}`,
            `--region=${config.app.awsRegion}`,
            `--profile=${config.app.awsProfile}`,
            `--msliceprofile=${config.app.awsMSLICEProfile}`,
            `--mslices3url=${config.app.awsMSLICES3Url}`],
          { cwd: config.app.binDir });

const src = path.join(config.app.binDir, cfgFile);
const dst = cfgFile;
fs.copySync(src, dst);
console.log(`copied '${src}' to '${dst}'`);
