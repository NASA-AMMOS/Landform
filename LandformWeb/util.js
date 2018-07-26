const spawn = require('child_process').spawn;
const path = require('path');
const byline = require('byline');

const config = require('./config');
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
//commandLine: whether to return results as an command line array of "-name value" pairs
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
    else if ('default' in dsc) val = dsc;
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
    Object.entries(ret).forEach(([n, v]) => { cl.push(`-${n}`); cl.push(v); });
    return cl;
  }

  return ret;
}

function sendJson(res, obj) { res.contentType('application/json').send(JSON.stringify(obj)); }

function sendText(res, txt) { res.contentType('text/plain').send(txt); }

function sendSuccess(res) { sendJson(res, { success: true }); }

//convenience to make an Error object that includes an HTTP status code, default 400 (invalid request)
function routeError(msg, status) { const e = new Error(msg); e.status = status || 400; return e; }

//convenience to log an error in a route and also send it as an HTTP response
//response status code is taken from err.status, default 500 (server error)
//the response is sent as a JSON object by default with success: false and an error message
//if the optional param text is true then the response is sent as a plain text error message
function abortRoute(res, prefix, err, text) {
  const msg = `${prefix}: ${err.message}`;
  const status = 'status' in err ? err.status : 500;
  logger.error(msg);
  if (logger.levels[logger.level] >= logger.levels.debug) console.error(err);
  res.status(status);
  if (!text) sendJson(res, { success: false, error: msg });
  else sendText(msg);
}

//spawn a subprocess
//the optional arg fmt is a function taking (cmd, args) and returning a human-readable name for the task
//otherwise name = cmd
//returns task object
let nextTask = 0; const tasks = {};
function launchTask(cmd, args, fmt) {

  const name = fmt ? fmt(cmd, args) : cmd;
  const id = nextTask++;
  const task = tasks[id] = {
    cmd, args,
    process: null,
    promise: null,
    info: { id, name, running: true, success: false, exitCode: null, error: null, started: Date.now(), ended: null },
    log: [],
    listeners: [],
    liveStreams: 2,
  };

  //forward spew from task to registered listeners
  //also logs it at debug level
  //line === null indicates to the listeners that the task is finished
  function log(line) {
    const done = line === null;
    //only really done when the process is dead and its output streams are all ended
    if (done && (task.liveStreams > 0 || task.process)) return;
    if (!done) logger.debug(line);
    task.listeners.forEach(l => l(line)); //forward on line = null to listeners if done
    if (done) task.listeners = [];
  }

  //at least one of code, signal, or err will be non-null
  //if code is non-null then the subprocess exited on its own with this code
  //if signal is non-null then the subprocess was terminated due to a signal with this name
  //if err is non-null then the process could not be spawned due to this Error
  function end(resolve, reject, code, signal, err) {
    if (!task.process) return; //already ended
    task.process = null;
    task.info.running = false;
    task.info.ended = Date.now();
    task.info.success = !code && !signal && !err;
    task.info.exitCode = code || signal || err.message;
    if (!task.info.success) task.info.error = `failed with error ${task.info.exitCode}`;
    const msg = `${name} ended at ${task.info.ended}${!task.info.success ? ', ' + task.info.error : ''}`;
    logger.verbose(msg);
    log(null);
    if (!task.info.success) reject(err || new Error(msg)); else resolve();
  }

  task.process = spawn(path.join(config.app.binDir, `${cmd}.exe`), args);

  task.promise = new Promise((resolve, reject) => {
    task.process.on('error', (err) => end(resolve, reject, null, null, err)); //might get both or either 'err', 'exit'
    task.process.on('exit', (code, signal) => end(resolve, reject, code, signal, null));
  });

  [task.process.stdout, task.process.stderr].forEach(stream => {
    byline(stream).on('data', line => log(line)).on('end', () => { task.liveStreams--; log(null); });
  });

  logger.verbose(`${name} started at ${task.info.started}`);

  return task;
}

function getTask(id) { return tasks[id]; }

//avoid memory leak, reap completed tasks that are too old
setInterval(() => {
  const tooOld = Date.now() - config.app.reapOldTasks * 1000;
  const dead = [];
  Object.values(tasks).forEach(task => {
    if (!task.info.running && task.info.ended < tooOld) dead.push(task.info.id);
  });
  dead.forEach(id => { delete tasks[id]; });
}, 60 * 1000);

module.exports = {
  parseBool,
  isNum, isInt,
  parseArgs,
  sendJson, sendText, sendSuccess,
  routeError, abortRoute,
  launchTask, getTask,
};
