const config = require('../config');
const { setTilingEnv } = require('../tilingUtil');
const { spawn } = require('./toolUtil');

//npm run delete-queues -- [venue]

const env = setTilingEnv();
if (process.argv.length > 2) env.LANDFORM_VENUE_NAME = process.argv[2];
const venue = env.LANDFORM_VENUE_NAME;

console.log(`deleting queues in venue ${venue}`);
spawn(config.exe, ['deletequeues'], { cwd: config.app.binDir, env });
