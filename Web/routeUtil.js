const logger = require('./logger');

function parseBool(v) { return v === 'true' || v === true; } //deal with 'false' not actually falsey

function isNum(n) { return !isNaN(n) && n !== null; } //eslint-disable-line no-restricted-globals

function isInt(n) { return isNum(n) && parseInt(n) === parseFloat(n); }

//accepts REST endpoint argument descriptors and attempts to parse them from req.query or req.body
//
//if an arg is both in req.query and req.body then the value from req.query will be used
//
//the descriptors are passed as a dictionary argName -> argDescriptor
//
//the descriptor objects can have the following fields:
//type: optional, one of 'enum', 'int', 'float', 'bool', 'string'
//required: optional, throw exception if arg is not found
//default: optional default value if arg is not found
//options: array of allowed values if type is 'enum'
//
//recognized options:
//commandLine: whether to return results as an command line array of "--name value" pairs
//ignoreQuery: don't consider req.query
//ignoreBody: don't consider req.body
//
//the default output is a dictionary argName -> argValue
function parseArgs(req, descriptors, opts) {

  const { commandLine, ignoreQuery, ignoreBody } = opts || {};
  const ret = {};

  Object.entries(descriptors).forEach(([name, dsc]) => {

    let val = null, skip = false;

    if (!ignoreQuery && name in req.query) val = req.query[name];
    else if (!ignoreBody && name in req.body) val = req.body[name];
    else if (dsc.required) throw routeError(`missing required parameter ${name}`);
    else if ('default' in dsc) val = dsc.default;
    else skip = true;

    if (!skip) {
      switch (dsc.type) {
        case 'enum': {
          if (!dsc.options.includes(val)) throw routeError(`invalid value '${val}' for parameter ${name}`);
          break;
        }
        case 'int': val = parseInt(val); break;
        case 'float': val = parseFloat(val); break;
        case 'bool': val = parseBool(val); break;
        case 'string': default: val = JSON.stringify(val); break;
      }
      ret[name] = val;
    }
  });

  if (commandLine) {
    const cl = [];
    Object.entries(ret).forEach(([n, v]) => { cl.push(`--${n}`); cl.push(v); });
    return cl;
  }

  return ret;
}

//send JSON object and end response
function sendJson(res, obj) { res.contentType('application/json').send(JSON.stringify(obj)); }

//send text and end response
function sendText(res, txt) { res.contentType('text/plain').send(txt); }

//send JSON success object and end response
function sendSuccess(res) { sendJson(res, { success: true }); }

//convenience to make an Error object that includes an HTTP status code, default 400 (invalid request)
function routeError(msg, status) { const e = new Error(msg); e.status = status || 400; return e; }

//convenience to log an error in a route and also send it as an HTTP response
//response status code is taken from err.status, default 500 (server error)
//the response is sent as a JSON object by default with success: false and an error message
//if the optional param text is true then the response is sent as a plain text error message
function abortRoute(res, msg, errOrStatus, text) {
  const err = errOrStatus instanceof Error ? errOrStatus : null;
  const status = isInt(errOrStatus) ? errOrStatus : (err && 'status' in err) ? err.status : 500;
  if (err && err.message) msg = `${msg}: ${err.message}`;
  logger.error(msg);
  //suppress detailed exception logging for Error objects that have a status set
  //the presence of a status code means that they were thrown by our own route error checking
  if (err && !('status' in err)) logger.exception(err);
  res.status(status);
  if (!text) sendJson(res, { success: false, error: msg });
  else sendText(msg);
}

module.exports = { parseBool, isNum, isInt, parseArgs, sendJson, sendText, sendSuccess, routeError, abortRoute };
