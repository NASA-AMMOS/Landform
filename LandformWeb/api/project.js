const express = require('express');
const path = require('path');
const fs = require('fs-extra');
const multer = require('multer');

const config = require('../config');
const { routeError, abortRoute, parseArgs } = require('../routeUtil');
const { launchTask, runTask } = require('../taskUtil');

//spawn TilingServer verb
//DynamoDB prefix and AWS profile name will be added to the command args
async function runTilingServer(req, res, verb, args, cleanup) {
  const task = launchTask('TilingServer', [verb, config.app.dbPrefix, ...args, '--profile', config.app.awsProfile],
                          { name: `TilingServer ${verb} ${args[0]}` }); //"TilingServer <verb> <project>"
  await runTask(req, res, task, cleanup);
}

const router = express.Router();

async function createProject(req, res) {
  try {

    const args = parseArgs(req, {
      tilingscheme: { type: 'enum', options: ['Bin', 'Quad', 'Oct', 'UserDefined'] },
      skirtmode: { type: 'enum', options: ['None', 'X', 'Y', 'Z'] },
      facespertile: { type: 'int' },
      tileresolution: { type: 'int' },
    }, { commandLine: true });

    await runTilingServer(req, res, 'createproject', [req.params.name, ...args]);

  } catch (e) { abortRoute(res, 'error creating project', e); }
}
router.post('/:name', createProject);

let nextUpload = 0;
async function makeTmpDir() {
  let tmpDir = null;
  do { tmpDir = path.join(config.app.uploadDir, `tmp${nextUpload++}`); } while (await fs.pathExists(tmpDir));
  await fs.ensureDir(tmpDir);
  return tmpDir;
}

async function uploadInput(req, res) {

  const paths = [];
  let tmpdir = null;

  let didCleanup = false;
  function cleanup() {
    if (!didCleanup) {
      didCleanup = true;
      paths.forEach(f => fs.remove(f));
      if (tmpdir) fs.remove(tmpdir);
    }
  }

  async function addFile(f) {
    const dest = path.join(tmpdir, f.originalname);
    await fs.move(f.path, dest);
    paths.push(dest);
  }

  try {

    //multer will have saved the files from the multipart body request into the upload dir
    //but the filenames will be arbitrary hashes at this point
    //so move them into a unique temp dir for this upload and rename them back to their original names
    //this is required so that they will have the correct names including filename extension when they make it to S3
    //ultimately the pipeline worker will look at that filename extension to determine the file format

    tmpdir = await makeTmpDir();

    //the mesh file is required
    if (!req.files || !req.files.mesh || req.files.mesh.length < 1) {
      throw routeError('upload does not include mesh file');
    }
    await addFile(req.files.mesh[0]);

    //the texture file is optional
    if (req.files.texture && req.files.texture.length > 0) await addFile(req.files.texture[0]);

    const args = parseArgs(req, { tileid: { type: 'string' } }, { commandLine: true });

    await runTilingServer(req, res, 'uploadinput', [req.params.name, ...paths, ...args], cleanup);

  } catch (e) { cleanup(); abortRoute(res, 'error processing upload', e); }
}
const multerConfig = { dest: config.app.uploadDir };
const multerFields = [{ name: 'mesh', maxCount: 1 }, { name: 'texture', maxCount: 1 }];
router.post('/:name/upload', multer(multerConfig).fields(multerFields), uploadInput);

async function runProject(req, res) {
  try { await runTilingServer(req, res, 'runproject', [req.params.name]); }
  catch (e) { abortRoute(res, 'error running project', e); }
}
router.post('/:name/run', runProject);

//TODO
//get project (metadata, status, progress, errors, etc)
//list projects
//trash/untrash project
//rename project
//trash/untrash input
//rename input
//download ouptput

module.exports = router;
