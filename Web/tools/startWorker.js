const config = require('../config');
const { setTilingEnv } = require('../tilingUtil');
const { spawnSync } = require('./deployUtil');

spawnSync('TilingServer.exe', ['startworker'], { cwd: config.app.binDir, env: setTilingEnv() });
