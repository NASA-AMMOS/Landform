const express = require('express');
const path = require('path');
const fs = require('fs-extra');
const multer = require('multer');
const url = require('url');

const config = require('../config');
const { routeError, abortRoute, parseArgs, sendJson } = require('../routeUtil');
const { taskHandler } = require('../taskUtil');
const { tilingTask, tilingErrorStatus: errorStatus } = require('../tilingUtil');

const router = express.Router();

async function createProject(req, res) {
  try {

    const args = parseArgs(req, {
      tilingscheme: { type: 'enum', options: ['Bin', 'QuadX', 'QuadY', 'QuadZ', 'Oct', 'UserDefined'] },
      skirtmode: { type: 'enum', options: ['None', 'Normal', 'X', 'Y', 'Z'] },
      reconmethod: { type: 'enum', options: ['Poisson', 'FSSR'] },
      facespertile: { type: 'int' },
      tileresolution: { type: 'int' },
      projecttype: { type: 'enum', options: ['GenericTiling', 'MSL'] },
      exportmeshformat: { type: 'enum', options: ['', 'obj', 'ply', 'stl'] },
      exportimageformat: { type: 'enum', options: ['', 'tif', 'png', 'jpg'] },
      maxleafgroupsize: { type: 'int' },
    }, { commandLine: true });

    const task = await tilingTask('createproject', [req.params.name, ...args]);

    await taskHandler(req, res, task, { errorStatus });

  } catch (e) { abortRoute(res, 'error creating project', e); }
}
router.post('/:name', createProject);

async function deleteProject(req, res) {
  try {
    //default timeout seems to be 2 min
    //https://github.com/expressjs/express/issues/3330
    //the c# commandlet implementation, in this case TileServer.DeleteProject, also has a corresponding timeout
    req.setTimeout(30 * 60 * 1000); //it can take a while to delete a big project
    const task = await tilingTask('deleteproject', [req.params.name]);
    await taskHandler(req, res, task, { errorStatus });
  } catch (e) { abortRoute(res, 'error deleting project', e); }
}
router.delete('/:name', deleteProject);

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

    const task = await tilingTask('uploadinput', [req.params.name, ...paths, ...args]);

    await taskHandler(req, res, task, { cleanup, errorStatus });

  } catch (e) { cleanup(); abortRoute(res, 'error processing upload', e); }
}
const multerConfig = { dest: config.app.uploadDir };
const multerFields = [{ name: 'mesh', maxCount: 1 }, { name: 'texture', maxCount: 1 }];
router.post('/:name/upload', multer(multerConfig).fields(multerFields), uploadInput);

async function runProject(req, res) {
  try {
    const task = await tilingTask('runproject', [req.params.name]);
    await taskHandler(req, res, task, { errorStatus });
  } catch (e) { abortRoute(res, 'error running project', e); }
}
router.post('/:name/run', runProject);

function resultURL(name) {
  const s3Bucket = new url.URL(config.app.s3Url).hostname;
  return `https://${s3Bucket}.s3.amazonaws.com/${config.app.venue}/www/${name}/tileset.json`;
}

router.get('/:name/result', (req, res) => {
  const redirect = parseArgs(req, { redirect: { type: 'bool', default: true } }).redirect;
  const ret = resultURL(req.params.name);
  if (redirect) res.redirect(ret); else sendJson(res, ret);
});

router.get('/:name/view', (req, res) => {
  const redirect = parseArgs(req, { redirect: { type: 'bool', default: true } }).redirect;
  const ret = `/viewer/index.html?TilesetURL=${encodeURIComponent(resultURL(req.params.name))}`;
  if (redirect) res.redirect(ret);
  else {
    //this is tricky
    //
    //we need a URL that will work no matter if we are deployed in production or running in a dev environment
    //
    //in dev environments typically the viewer is served by a webpack dev server running on port 3000
    //but it is hard to know that for sure here
    //
    //in dev typically the REST API (this server itself) is running on port config.app.port (typically 8081)
    //but that is also true in EB deployments because typically in that case we are behind an nginx reverse proxy
    //actually in EB deployments typically the reverse proxying is 443 (nginx) -> 80 (docker) -> 8081 (node)
    //
    //so for dev the URL needs to include a port, ideally 3000
    //but because that is hard to know we'll settle for config.app.port, which will also work
    //**because in dev contexts the REST API server will also serve the viewer at the same relative path
    //
    //so we "just" need to answer the question "are we running in a deployment or in a dev environment?"
    //typically this would be answered by NODE_ENV
    //however, when testing a deployment locally with docker, NODE_ENV=production, but we still need port=8081
    //so make the call based on the original request protocol
    //because only EB deployments actually implement https
    let base = `${req.protocol}://${req.hostname}`;
    if (req.protocol !== 'https') base += `:${config.app.port}`;
    sendJson(res, `${base}${ret}`);
  }
});

async function projectMetadata(req, res) {
  try {
    const task = await tilingTask('projectmetadata', [req.params.name, '--quiet']);
    await taskHandler(req, res, task, { pipe: true, exposeArgs: [], errorStatus });
  } catch (e) { abortRoute(res, 'error getting project metadata', e); }
}
router.get('/:name', projectMetadata);

async function listProjects(req, res) {
  try {
    const task = await tilingTask('listprojects', ['--quiet']);
    await taskHandler(req, res, task, { pipe: true, exposeArgs: [], errorStatus });
  } catch (e) { abortRoute(res, 'error listing projects', e); }
}
router.get('/', listProjects);

module.exports = router;
