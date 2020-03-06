const config = require('../config');
const { setTilingEnv } = require('../tilingUtil');
const { spawn } = require('./toolUtil');

//npm run delete-queues -- [venue]

const env = setTilingEnv();
if (process.argv.length > 2) env.LANDFORM_VENUE = process.argv[2];
const venue = env.LANDFORM_VENUE;

console.log(`deleting queues in venue ${venue}`);
spawn(config.app.masterExe, ['deletequeues'], { cwd: config.app.binDir, env });
