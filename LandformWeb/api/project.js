const express = require('express');
const fs = require('fs-extra');
const multer = require('multer');

const config = require('../config');
const { routeError, abortRoute, parseArgs } = require('../routeUtil');
const { launchTask, runTask } = require('../taskUtil');

//spawn TilingServer verb
//DynamoDB prefix and AWS profile name will be added to the command args
async function runTilingServer(req, res, verb, args, cleanup) {
  const task = launchTask('TilingServer', [verb, config.app.dbPrefix, ...args, '--Profile', config.app.awsProfile],
                          (c, a) => `${c} ${a[0]} ${a[2]}`); //task name is "TilingServer <verb> <project>"
  await runTask(req, res, task, cleanup);
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

    await runTilingServer(req, res, 'createproject', [req.params.name, ...args]);

  } catch (e) { abortRoute(res, 'error creating project', e); }
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

    await runTilingServer(req, res, 'uploadinput', [req.params.name, ...paths, ...args],
                  () => paths.forEach(f => fs.remove(f)));

  } catch (e) { abortRoute(res, 'error processing upload', e); }
}
const multerConfig = { dest: config.app.uploadDir };
const multerFields = [{ name: 'mesh', maxCount: 1 }, { name: 'texture', maxCount: 1 }];
router.post('/:name/upload', multer(multerConfig).fields(multerFields), uploadInput);

async function runProject(req, res) {
  try { await runTilingServer(req, res, 'runproject', [req.params.name]); }
  catch (e) { abortRoute(res, 'error running project', e); }
}
router.get('/:name/run', runProject);
router.post('/:name/run', runProject);

//TODO
//get project
//list projects
//trash/untrash project
//rename project
//trash input
//download ouptput

module.exports = router;
