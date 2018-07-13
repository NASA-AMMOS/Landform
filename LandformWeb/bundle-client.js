const Zip = require('@jpl/adm-zip'); // eslint-disable-line import/no-extraneous-dependencies
const path = require('path');

const f = 'landformweb.zip';
const d = path.join('client', 'build');

const z = new Zip(f);
z.addLocalFolder(d, d);
z.writeZip(f);
z.getEntries().forEach(e => console.log(e.entryName)); // eslint-disable-line no-console
