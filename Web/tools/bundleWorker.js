const fs = require('fs-extra');
const Zip = require('@jpl/adm-zip');

const config = require('../config');
const { prompt } = require('./toolUtil');

const bundle = config.app.workerBundle;
const binDir = config.app.binDir;

function zipIt() {
  console.log(`zipping '${binDir}/*' to '${bundle}'`);
  const z = new Zip();
  z.addLocalFolder(binDir, '', (n) => !n.startsWith('tmp') && !(n.startsWith('log') && n.endsWith('.txt')));
  z.writeZip(bundle);
  //z.getEntries().forEach(e => console.log(e.entryName));
}

if (fs.pathExistsSync(binDir)) {
  if (!fs.pathExistsSync(bundle)) zipIt();
  else prompt('bundleWorker', `overwrite ${bundle}`).then(ok => { if (ok) { fs.removeSync(bundle); zipIt(); } });
} else console.log(`'${binDir}' not found`);
