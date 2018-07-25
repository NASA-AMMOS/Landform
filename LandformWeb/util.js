const config = require('./config');

function routeError(msg, status) { const e = new Error(msg); e.status = status || 400; return e; }

function abortRoute(res, prefix, err) {
  const msg = `${prefix}: ${err.message}`;
  const status = 'status' in err ? err.status : 500;
  console.error(msg);
  if (config.app.verboseLogging) console.error(err);
  res.status(status).contentType('application/json').send(JSON.stringify({ error: msg }));
}

function sendJson(res, obj) { res.contentType('application/json').send(JSON.stringify(obj)); }

function parseBool(v) { return v === 'true' || v === true; }

function isSet(v) { return v !== undefined && v !== null; } //deal with fields that can be e.g. 0 or false

function parseArgs(obj, args) {
  const ret = [];
  args.forEach(arg => {
    let val = obj[arg.name];
    if (isSet(val)) {
      switch (arg.type) {
        case 'enum': {
          if (!arg.options.includes(val)) throw routeError(`invalid value '${val}' for parameter ${arg.name}`);
          break;
        }
        case 'int': val = parseInt(val); break;
        case 'float': val = parseFloat(val); break;
        case 'bool': val = parseBool(val); break;
        case 'string': default: break;
      }
      ret.push(`-${arg.name}`);
      ret.push(val);
    } else if (arg.required) throw routeError(`missing required parameter ${arg.name}`);
  });
  return ret;
}

function launchTask(command, args) {
  //TODO
}

function taskInfo(id) {
  //TODO
}

function taskLog(id) {
  //TODO
}

module.exports = { routeError, abortRoute, sendJson, parseBool, isSet, parseArgs, launchTask, taskInfo, taskLog };
