const express = require('express');
const fs = require('fs-extra');
const multer = require('multer');

const config = require('./config');
const { sendJson, sendSuccess, routeError, abortRoute, parseArgs, launchTask } = require('./util');

//spawn TilingServer verb in a subprocess
//DynamoDB prefix and AWS profile name will be added to the command args
//optional function cleanup will be run when the task completes (success or fail)
//if the request includes a query or body param async=true then the HTTP request will be ended now with the task info
//otherwise the HTTP request will end when the task ends with a JSON object
//the returned object will always include success: true|false
//on error the object will also include error: message
async function runTask(req, res, verb, args, cleanup) {

  const task = launchTask('TilingServer', [verb, config.app.dbPrefix, ...args, '--Profile', config.app.awsProfile],
                          (c, a) => `${c} ${a[0]} ${a[2]}`); //task name is "TilingServer <verb> <project>"

  if (cleanup) task.promise.finally(cleanup);

  if (parseArgs(req, { async: { type: 'bool', default: false } }).async) {
    //async task errors are handled elsewhere
    task.promise.catch(() => {}); //just swallow exception here to prevent unhandled promise rejection warning
    sendJson(res, task.info);
  } else {
    await task.promise; //will throw on task error, triggering abortRoute()
    sendSuccess(res);
  }
}

const router = express.Router();

async function createProject(req, res) {
  try {

    const args = parseArgs(req, {
      TilingScheme: { type: 'enum', required: false, options: ['Bin', 'Quad', 'Oct', 'UserDefined'] },
      SkirtMode: { type: 'enum', required: false, options: ['None', 'X', 'Y', 'Z'] },
      FacesPerTile: { type: 'int', required: false },
      TileResolution: { type: 'int', required: false },
    }, { commandLine: true });

    await runTask(req, res, 'createproject', [req.params.name, ...args]);

  } catch (e) { abortRoute(res, 'failed to create project', e); }
}
router.post('/:name', createProject);
router.put('/:name', createProject);

async function uploadInput(req, res) {
  try {

    const paths = [];

    if (!req.files.mesh || req.files.mesh.length < 1) throw routeError('upload does not include mesh file');
    paths.push(req.files.mesh[0].path);

    if (req.files.texture && req.files.texture.length > 0) paths.push(req.files.texture[0].path);

    const args = parseArgs(req, { TileId: { type: 'string', required: false } }, { commandLine: true });

    await runTask(req, res, 'uploadinput', [req.params.name, ...paths, ...args],
                  () => paths.forEach(f => fs.remove(f)));

  } catch (e) { abortRoute(res, 'failed to process upload', e); }
}
const multerConfig = { dest: config.app.uploadDir };
const multerFields = [{ name: 'mesh', maxCount: 1 }, { name: 'texture', maxCount: 1 }];
router.post('/:name', multer(multerConfig).fields(multerFields), uploadInput);

async function runProject(req, res) {
  try { await runTask(req, res, 'runproject', [req.params.name]); }
  catch (e) { abortRoute(res, 'failed to run project', e); }
}
router.get('/:name', runProject);
router.post('/:name', runProject);

module.exports = router;
