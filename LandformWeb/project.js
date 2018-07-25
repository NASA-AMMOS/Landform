const express = require('express');
const fs = require('fs-extra');
const multer = require('multer');

const config = require('./config');
const { routeError, abortRoute, sendJson, parseBool, parseArgs, launchTask } = require('./util');

async function runTask(req, res, verb, args, cleanup) {
  const task = launchTask('TilingServer', [verb, config.app.dbPrefix, ...args, '--Profile', config.app.awsProfile]);
  if (cleanup) task.finally(cleanup);
  if (parseBool(req.body.sync)) await task.promise();
  sendJson(res, task);
}

const router = express.Router();

async function createProject(req, res) {
  try {

    const args = parseArgs(req.body, [
      { name: 'TilingScheme', type: 'enum', required: false, options: ['Bin', 'Quad', 'Oct', 'UserDefined'], },
      { name: 'SkirtMode', type: 'enum', required: false, options: ['None', 'X', 'Y', 'Z'], },
      { name: 'FacesPerTile', type: 'int', required: false },
      { name: 'TileResolution', type: 'int', required: false },
    ]);

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

    const args = parseArgs(req.body, [{ name: 'TileId', type: 'string', required: false }]);

    await runTask(req, res, 'uploadinput', [req.params.name, ...paths, ...args],
                  () => paths.forEach(f => fs.remove(f)));

  } catch (e) { abortRoute(res, 'failed to process upload', e); }
}
//note: config.app.maxBodySize is configured on body-parser middleware in server.js
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
