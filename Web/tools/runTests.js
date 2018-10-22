const fs = require('fs-extra');
const path = require('path');
const newman = require('newman');
const { parseSec, timeStamp, timeFmt } = require('../timeUtil');
const { hasFlag, deepCopy, checkTTY, readJson } = require('./toolUtil');

const deleteCollection = require('../test/Landform-delete.postman_collection.json');
const runCollection = require('../test/Landform-run.postman_collection.json');
const pollCollection = require('../test/Landform-poll.postman_collection.json');

const cfgFile = 'landform-test-config.json';
const projCfgFile = 'landform-project-config.json';
const uploadItemName = 'upload data';
const pollSec = 10;

const argv = process.argv;

function usage(err) {
  console.log('USAGE: runTests [dir] [-h|--help] [-d|--dry-run]');
  process.exit(err);
}

if (hasFlag('help')) usage();

const dryRun = hasFlag('dry-run');

if (argv.length > 2 && !argv[2].startsWith('-')) {
  const dir = argv[2];
  if (!fs.pathExistsSync(dir)) {
    console.error(`directory '${dir}' not found`);
    usage(1);
  }
  process.chdir(dir);
}

const testDir = process.cwd();

if (!fs.pathExistsSync(cfgFile)) {
  console.error(`config file '${cfgFile}' not found in '${testDir}'`);
  process.exit(2);
}

const testCfg = readJson(cfgFile);

const stamp = timeStamp();

function parseJsonResponse(ex) {
  if (ex.response && Buffer.isBuffer(ex.response.stream)) {
    try { ex.response.json = JSON.parse(ex.response.stream.toString('utf8')); }
    catch (e) { ex.response.json = `error parsing response stream as JSON: ${e.message || e}`; }
  }
  return ex;
}

function runTest(projDir) {
  return new Promise(resolve => {

    let projName = projDir, maxMS = 0; //will be updated after we read project config file
    const fullPath = path.join(testDir, projDir);
    const start = Date.now(), elapsed = () => Date.now() - start;

    const done = () => {
      console.log(`${timeStamp()}: finished ${projName} in ${fullPath}, total time ${timeFmt(elapsed())}`);
      process.chdir(testDir);
      resolve();
    };

    const error = e => {
      const msg = `error running ${projName} in ${fullPath}: ${e.message || e}`;
      console.error(msg);
      const info = { error: msg };
      fs.writeJsonSync(`log-${projName}-${stamp}-fail.txt`, info, { spaces: 2 }); //if no projDir will be in testDir
      done(); //move on to next test even if this one errored
    };

    const run = opts => { //run Postman collection

      const coll = opts.collection.info.name;

      opts.timeout = maxMS - elapsed();

      console.log(`${timeStamp()}: running ${coll} for ${projName} in ${fullPath}, max time ${timeFmt(opts.timeout)}`);

      return new Promise((_resolve, reject) => {
        const _done = (err, summary) => {

          err = err || summary.error;
          if (err) {
            if (opts.bail || opts.abortOnError || opts.abortOnFailure) { reject(err); return; }
            console.warn(`ignoring error: ${err.message || err}`);
          }

          if (summary.run && summary.run.failures && summary.run.failures.length > 0) {
            err = summary.run.failures[0].error || 'unknown test assertion failure';
            if (err) {
              if (opts.bail || opts.abortOnFailure) { reject(err); return; }
              console.warn(`ignoring error: ${err.message || err}`);
            }
          }

          if (summary.run && summary.run.executions) {
            if (opts.omitExecutions) delete summary.run.executions;
            else summary.run.executions = summary.run.executions.map(ex => parseJsonResponse(ex));
          }

          fs.writeJsonSync(`log-${projName}-${coll}-${stamp}.txt`, summary, { spaces: 2 });

          _resolve();
        };

        if (dryRun) _done(null, { dryRun: true, ...opts });
        else newman.run(opts, _done);

      });
    };

    try {

      if (!fs.pathExistsSync(projDir)) throw new Error(`subdirectory '${projDir}' not found`);

      if (!fs.pathExistsSync(path.join(fullPath, projCfgFile))) {
        throw new Error(`config file ${projCfgFile} not found in ${fullPath}`);
      }

      process.chdir(fullPath);

      const projCfg = readJson(projCfgFile);
      projName = projCfg.name;
      maxMS = parseSec(projCfg.maxTime) * 1000;

      console.log(`${timeStamp()}: running ${projName} in ${fullPath}, max time ${projCfg.maxTime}`);

      //configure inputs
      const projCollection = deepCopy(runCollection);
      const items = projCollection.item;
      const inputIndex = items.findIndex(item => item.name === uploadItemName);
      if (inputIndex < 0) throw new Error(`'${uploadItemName}' not found in ${projCollection.info.name}`);
      const uploadItem = items[inputIndex], uploadItems = [];
      const numInputs = projCfg.inputs.length;
      for (let i = 0; i < numInputs; i++) {
        const item = deepCopy(uploadItem);
        uploadItems.push(item);
        item.name = `upload data (${i + 1}/${numInputs})`;
        const { mesh, texture, options } = projCfg.inputs[i];
        item.request.body.formdata.find(d => d.key === 'mesh').src = mesh;
        item.request.body.formdata.find(d => d.key === 'texture').src = texture;
        if (options) {
          item.event.find(e => e.listen === 'prerequest').script.exec = [
            `pm.environment.set(\\"UPLOAD_OPTIONS\\", \\"${options}\\");`,
          ];
        }
      }
      projCollection.item = items.slice(0, inputIndex).concat(uploadItems, items.slice(inputIndex + 1));

      const environment = {
        values: [
          { key: 'SERVER_URL', value: testCfg.serverUrl },
          { key: 'API_TOKEN', value: testCfg.apiToken },
          { key: 'PROJECT_NAME', value: projName },
          { key: 'PROJECT_OPTIONS', value: projCfg.options },
        ],
      };

      const abortOnError = true;
      const abortOnAny = { bail: true, abortOnError: true, abortOnFailure: true };
      const delayRequest = pollSec * 1000, omitExecutions = true;
      run({ environment, abortOnError, collection: deleteCollection }) //don't bail if delete fails (e.g. on first run)
        .then(() => run({ environment, ...abortOnAny, collection: projCollection }))
        .then(() => run({ environment, ...abortOnAny, delayRequest, omitExecutions, collection: pollCollection }))
        .then(done)
        .catch(error);

    } catch (e) { error(e); }
  });
}

checkTTY('runTests') //ctrl-c doesn't work right in cygwin
  .then(ok => {
    if (ok) {
      console.log(`${stamp}: running ${testCfg.testDirs.length} projects in ${testDir}`);
      console.log(`server: ${testCfg.serverUrl}`);
      if (dryRun) console.log('dry run');
      testCfg.testDirs.reduce((promise, dir) => promise.then(() => runTest(dir)), Promise.resolve());
    }
  });

