const config = require('./config');
const logger = require('./logger');
const { launchTask } = require('./taskUtil');

let masterTask = null;

function fail(err) {
  logger.error(`aborting, error launching 'TilingServer startmaster': ${err.message}`);
  logger.exception(err);
  process.exit(1);
}

try {
  masterTask = launchTask('TilingServer', ['startmaster', config.app.dbPrefix, '--profile', config.app.awsProfile],
                          (c, a) => `${c} ${a[0]}`); //task name is "TilingServer <verb>"
  masterTask.promise.catch(err => fail(err));
} catch (err) { fail(err); }

module.exports = masterTask;
