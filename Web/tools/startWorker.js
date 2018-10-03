const config = require('../config');
const { setTilingEnv } = require('../tilingUtil');
const { spawn } = require('./deployUtil');

//npm run start-worker -- [venue-name]

const env = setTilingEnv();
if (process.argv.length > 2) env.TILE_SERVER_VENUE_NAME = process.argv[2];
const venue = env.TILE_SERVER_VENUE_NAME;

console.log(`starting worker in venue ${venue}`);
spawn('TilingServer.exe', ['startworker'], { cwd: config.app.binDir, env });
