const config = require('../config');
const { setTilingEnv } = require('../tilingUtil');
const { spawn } = require('./toolUtil');

//npm run start-worker -- [venue]

const env = setTilingEnv();
if (process.argv.length > 2) env.LANDFORM_VENUE = process.argv[2];
const venue = env.LANDFORM_VENUE;

console.log(`starting worker in venue ${venue}`);
spawn(config.app.workerExe, ['startworker'], { cwd: config.app.binDir, env });
