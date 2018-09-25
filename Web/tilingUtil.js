const fs = require('fs-extra');
const config = require('./config');
const logger = require('./logger');
const { launchTask } = require('./taskUtil');

function setTilingEnv(env) {

  env = env || process.env;

  //if any of these vars are not set then set them from the indicated key in config.app
  const vars = {
    TILE_SERVER_REGION: 'awsRegion',
    TILE_SERVER_PROFILE: 'awsProfile',
    TILE_SERVER_VENUE_NAME: 'venueName',
    TILE_SERVER_S3_URL: 's3Url',
    TILE_SERVER_MSLICE_PROFILE: 'awsMSLICEProfile',
    TILE_SERVER_MSLICE_S3_URL: 'awsMSLICES3Url',
  };
  Object.entries(vars).forEach(([varName, cfgKey]) => { if (!(varName in env)) env[varName] = config.app[cfgKey]; });

  return env;
}

//creates tmpDir if necessary, then runs TilingServer verb args in it with appropriate environment vars
//waits for tmpDir to be created but does not wait for task to complete
//returns task
async function tilingTask(verb, args) {
  await fs.ensureDir(config.app.tmpDir);
  return launchTask('TilingServer', [verb, ...args], { cwd: config.app.tmpDir, env: setTilingEnv() });
}

let masterTask = null;
async function tilingMaster() {

  if (masterTask) return masterTask;

  function abort(err) {
    //make sure spew gets to log even when log level is < debug, as in production
    if (logger.levels[logger.level] < logger.levels.debug) logger.error(masterTask.log.join('\n'));
    logger.error(`aborting, error launching 'TilingServer startmaster': ${err.message}`);
    logger.exception(err);
    process.exit(1);
  }

  try {
    masterTask = await tilingTask('startmaster', [`--singlethreaded=${!config.app.multiThreadedMaster}`]);
    masterTask.promise.catch(err => abort(err));
  } catch (err) { abort(err); }

  return masterTask;
}

//map exit code to http status code
function tilingErrorStatus(code) { return code === 1 ? 400 : 500; }

module.exports = { setTilingEnv, tilingTask, tilingMaster, tilingErrorStatus };
