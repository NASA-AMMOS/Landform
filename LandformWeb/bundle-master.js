const fs = require('fs-extra');
const path = require('path');
const Zip = require('@jpl/adm-zip');

const bundle = require('./config').app.bundle;

//the "bundle" script in package.json creates the app zip bundle as a git archive of HEAD
//our task here is to add the bin/ subtree to it

const dbg = process.argv.slice(2).some(a => {
  const la = a.toLowerCase();
  return la === '-debug' || la === '--debug' || la === '--debug=true';
});

const binDir = path.join('..', 'TilingServer', 'bin', dbg ? 'Debug' : 'Release');

//copying only selected files results in a zip that is 100x smaller (1.7M vs 170M)
const files = [
  'AWSSDK.Core.dll', 'AWSSDK.DynamoDBv2.dll', 'AWSSDK.S3.dll', 'AWSSDK.SQS.dll',
  'Cloud.dll', 'CloudMessages.dll',
  'Geometry.dll', 'GeometryThirdparty.dll', 'Imaging.dll', 'MathExtensions.dll',
  'Pipeline.dll', 'Plumbing.dll', 'Util.dll', 'CommandLine.dll',
  'TilingServer.exe', 'TilingServer.exe.config',
  'Newtonsoft.Json.dll', 'log4net.dll', 'Xna.dll',
];

if (fs.pathExistsSync(bundle) && fs.pathExistsSync(binDir)) {

  console.log(`adding selected files from '${binDir}' to 'bin/' in '${bundle}'`);

  const z = new Zip(bundle);

  const src = fs.realpathSync(path.join(process.cwd(), binDir));

  let nextTmpDir = 0, tmpDir = null;
  do { tmpDir = path.join('tmp', `bundle-master${nextTmpDir++}`); } while (fs.pathExistsSync(tmpDir));
  fs.ensureDirSync(tmpDir);

  try {
    //fs.copySync(src, tmpDir);
    files.forEach(f => fs.copySync(path.join(src, f), path.join(tmpDir, f)));
    z.addLocalFolder(tmpDir, 'bin');
  } finally { fs.remove(tmpDir); }

  z.writeZip(bundle);

  //z.getEntries().forEach(e => console.log(e.entryName));

} else console.log(`cannot add '${binDir}' to '${bundle}', one or both missing`);
