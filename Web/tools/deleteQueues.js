const config = require('../config');
const { setTilingEnv } = require('../tilingUtil');
const { spawn } = require('./toolUtil');

//npm run delete-queues -- [venue-name]

const env = setTilingEnv();
if (process.argv.length > 2) env.TILE_SERVER_VENUE_NAME = process.argv[2];
const venue = env.TILE_SERVER_VENUE_NAME;

spawn('TilingServer.exe', ['deletequeues'], { cwd: config.app.binDir, env });
