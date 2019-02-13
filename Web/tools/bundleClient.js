const fs = require('fs-extra');
const path = require('path');
const Zip = require('@jpl/adm-zip');

const bundle = require('../config').app.masterBundle;
const { prompt, hasFlag } = require('./toolUtil');

//the "bundle" script in package.json creates the app zip bundle as a git archive of HEAD
//our task here is to add the client/build/ subtree to it

const clientDir = path.join('client', 'build');

function zipIt() {
  console.log(`adding '${clientDir}' to '${bundle}'`);
  const z = new Zip(bundle);
  z.addLocalFolder(clientDir, clientDir);
  z.writeZip(bundle);
  //z.getEntries().forEach(e => console.log(e.entryName));
}

if (fs.pathExistsSync(clientDir)) {
  if (!fs.pathExistsSync(bundle) || hasFlag('overwrite')) zipIt();
  else prompt('bundleClient', `overwrite ${bundle}`).then(ok => { if (ok) { fs.removeSync(bundle); zipIt(); } });
} else console.log(`'${clientDir}' not found`);
