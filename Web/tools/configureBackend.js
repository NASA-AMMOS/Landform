const config = require('../config');
const { spawnSync } = require('./deployUtil');

spawnSync('TilingServer.exe',
          ['configure',
            `--venuename=${config.app.venueName}`,
            `--s3url=${config.app.s3Url}`,
            `--region=${config.app.awsRegion}`,
            `--profile=${config.app.awsProfile}`,
            `--msliceprofile=${config.app.awsMSLICEProfile}`,
            `--mslices3url=${config.app.awsMSLICES3Url}`],
          { cwd: config.app.binDir });
