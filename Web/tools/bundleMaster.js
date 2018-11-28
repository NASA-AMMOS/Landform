const fs = require('fs-extra');
const Zip = require('@jpl/adm-zip');

const config = require('../config');
const { prompt, hasFlag, binFileFilter } = require('./toolUtil');

const bundle = config.app.bundle;
const binDir = config.app.binDir;

//the "bundle" script in package.json creates the app zip bundle as a git archive of HEAD
//our task here is to add the bin/ subtree to it

function zipIt() {
  console.log(`zipping '${binDir}/*' to 'bin/' in '${bundle}'`);
  const z = new Zip(bundle);
  z.addLocalFolder(binDir, 'bin', n => binFileFilter(n) && !n.startsWith('ExternalApps'));
  z.writeZip(bundle);
  //z.getEntries().forEach(e => console.log(e.entryName));
}

if (fs.pathExistsSync(binDir)) {
  if (!fs.pathExistsSync(bundle) || hasFlag('overwrite')) zipIt();
  else prompt('bundleMaster', `overwrite ${bundle}`).then(ok => { if (ok) { fs.removeSync(bundle); zipIt(); } });
} else console.log(`'${binDir}' not found`);
