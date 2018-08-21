const fs = require('fs-extra');
const path = require('path');
const Zip = require('@jpl/adm-zip');

const bundle = require('./config').app.bundle;

//the "bundle" script in package.json creates the app zip bundle as a git archive of HEAD
//our task here is to add the client/build/ subtree to it

const clientBuild = path.join('client', 'build');

if (fs.pathExistsSync(bundle) && fs.pathExistsSync(clientBuild)) {
  console.log(`adding '${clientBuild}' to '${bundle}'`);
  const z = new Zip(bundle);
  z.addLocalFolder(clientBuild, clientBuild);
  z.writeZip(bundle);
  //z.getEntries().forEach(e => console.log(e.entryName));
} else console.log(`cannot add '${clientBuild}' to '${bundle}', one or both missing (run 'npm build-client')`);
