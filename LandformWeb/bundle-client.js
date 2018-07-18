const Zip = require('@jpl/adm-zip');
const path = require('path');

const f = 'landformweb.zip';
const d = path.join('client', 'build');

const z = new Zip(f);
z.addLocalFolder(d, d);
z.writeZip(f);
z.getEntries().forEach(e => console.log(e.entryName));
