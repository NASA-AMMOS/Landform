const spawn = require('child_process').spawn;
const path = require('path');
const fs = require('fs-extra');
const byline = require('byline');

const config = require('./config');
const logger = require('./logger');
const { parseArgs, sendText, sendJson } = require('./routeUtil');

let nextTask = 0;
const tasks = {};

function getTask(id) { return tasks[id]; }

//spawn a subprocess
//options are passed to child_process.spawn()
//cmd must be an executable in config.app.binDir
//.exe will be appended if appropriate
//if not running on windows and the command is an exe then mono will be prepended to the command line
//returns task object
async function launchTask(cmd, args, options) {

  cmd = path.join(config.app.binDir, cmd);

  if (!(await fs.pathExists(cmd)) && (await fs.pathExists(`${cmd}.exe`))) cmd = `${cmd}.exe`;

  if (cmd.toLowerCase().endsWith('exe') && !process.platform.startsWith('win')) {
    cmd = 'mono';
    args.unshift(cmd);
  }

  const id = nextTask++;
  const task = tasks[id] = {
    cmd, args,
    process: null,
    promise: null,
    info: { id, running: true, success: false, exitCode: null, error: null, started: Date.now(), ended: null },
    log: [],
    listeners: [],
    liveStreams: 2,
  };

  //forward spew from task to registered listeners
  //also logs it at debug level
  //line = null tells listeners task is finished
  function log(line) {
    if (Buffer.isBuffer(line)) line = line.toString('utf8');
    const done = line === null;
    //only really done when the process is dead and its output streams are all ended
    if (done && (task.liveStreams > 0 || task.process)) return;
    if (!done) logger.debug(line); //TODO prob put task spew into a separate log
    task.listeners.forEach(l => l(line)); //forward on line = null to listeners if done
    if (!done) task.log.push(line);
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
    task.info.exitCode = task.info.success ? 0 : (code || signal || (err && err.message) || 'unknown');
    if (!task.info.success) {
      task.info.error =
        err && err.message ? err.message : code ? `code ${code}` : signal ? `signal ${signal}` : 'unknown';
    }
    logger.verbose(`task ${task.info.id} '${cmd} ${args.join(' ')}' ended at ${task.info.ended}` +
                   (task.info.success ? '' : `, ERROR: ${task.info.error}`));
    log(null);
    if (!task.info.success) reject(err || new Error(`task ${task.info.error}`)); else resolve();
  }

  task.process = spawn(cmd, args, options);

  task.promise = new Promise((resolve, reject) => {
    //might get both or either 'error' and/or 'exit' events
    task.process.on('error', (err) => end(resolve, reject, null, null, err));
    task.process.on('exit', (code, signal) => end(resolve, reject, code, signal, null));
  });

  [task.process.stdout, task.process.stderr].forEach(stream => {
    byline(stream).on('data', line => log(line)).on('end', () => { task.liveStreams--; log(null); });
  });

  logger.verbose(`task ${task.info.id} '${cmd} ${args.join(' ')}' started at ${task.info.started}`);

  return task;
}

//avoid memory leak, reap completed tasks
setInterval(() => {
  const tooOld = Date.now() - config.app.reapOldTasks * 1000;
  const dead = [];
  Object.values(tasks).forEach(task => {
    if (!task.info.running && task.info.ended < tooOld) dead.push(task.info.id);
  });
  dead.forEach(id => { delete tasks[id]; });
}, 60 * 1000);

//generic handler for API calls that run a task
//
//optional function cleanup will be run when the task completes (success or fail)
//
//if the request includes a query or body param async=true then the HTTP request will be ended now with the task.info
//otherwise the HTTP request will end when the task ends
//
//if no text=true or live=true param then the request will end when the task ends and return a json task.info object
//with success=true|false and errror=string in the case of failure
//
//if text=true then the request will return the log spew of the task
//
//if live=true then the request will return as plaintext
//1) the current json task.info
//2) the the log spew of the task as it is produced
//3) the final json task.info
//
//in all the non-async cases an Error will be thrown if the task errors
async function taskHandler(req, res, task, cleanup) {

  if (cleanup) {
    let didCleanup = false;
    const doCleanup = () => { if (!didCleanup) { didCleanup = true; cleanup(); } };
    task.promise.then(doCleanup).catch(doCleanup);
  }

  const { text, live, async } = parseArgs(req, {
    text: { type: 'bool', default: false },
    live: { type: 'bool', default: false },
    async: { type: 'bool', default: false },
  });

  if (async) {

    //just swallow exceptions here to prevent unhandled promise rejection warnings
    //they will also be recorded in task.info for potential later async retrieval
    task.promise.catch(() => {});

    //send task info and end response
    if (text) sendText(res, JSON.stringify(task.info));
    else sendJson(res, task.info);

  } else if (live) {

    res.contentType('text/plain');

    res.write(`ID: ${task.info.id}`);

    let err = null;
    task.promise.catch(e => { err = e; });

    function end() {
      if (err) res.write(`\nERROR: ${err.message}`);
      res.end();
    }

    const running = task.info.running;
    if (running) task.listeners.push(msg => { if (msg !== null) res.write(`\n${msg}`); else end(); }); //'' is falsey

    if (task.log.length > 0) res.write(`\n${task.log.join('\n')}`);

    if (!running) end();

  } else { //blocking task execution, json or text output

    //wait for task to complete
    let err = null;
    try { await task.promise; } catch (e) { err = e; res.status(500); }

    //send results and end response
    if (!text) sendJson(res, task.info);
    else {
      res.contentType('text/plain');
      res.write(`ID: ${task.info.id}`);
      res.write(`\n${task.log.join('\n')}`);
      if (err) res.write(`${task.log.length > 0 ? '\n' : ''}ERROR: ${err.message}`);
      res.end();
    }
  }
}

module.exports = { getTask, launchTask, taskHandler };
