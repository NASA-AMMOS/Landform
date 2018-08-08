const Zip = require('@jpl/adm-zip');
const path = require('path');

const bundle = require('./config').app.bundle;

const clientBuild = path.join('client', 'build');

const z = new Zip(bundle);
z.addLocalFolder(clientBuild, clientBuild);
z.writeZip(bundle);
z.getEntries().forEach(e => console.log(e.entryName));
