const execSync = require('child_process').execSync;
const fs = require('fs-extra');

const bundle = require('./config').app.bundle;

function boolArg(v, name) {
  const lv = v.toLowerCase(), ln = name.toLowerCase();
  return lv === `-${ln[0]}` || lv === `--${ln}` || lv === `--${ln}=true`;
}

async function checkDeploy() {

  let force = false;
  const args = process.argv.slice(2).reduce((a, v) => {
    if (boolArg(v, 'force')) force = true;
    else a.push(v);
    return a;
  }, []);

  if (!fs.pathExistsSync(bundle)) throw new Error(`${bundle} not found, run 'npm run build'`);

  try {

    const ht = new Date(1000 * await execSync('git show -s --format="%ct" HEAD'));
    const bt = fs.statSync(bundle).mtime;
    if (ht > bt) {
      throw new Error(`HEAD newer than ${bundle}: ${ht.toLocaleString()} > ${bt.toLocaleString()}\n` +
                      `update ${bundle} with 'npm run build'`);
    }

    const modified = (await execSync('git diff-index --name-status HEAD --')).toString('utf8');
    if (modified) {
      throw new Error(`uncommitted changes:\n${modified}\n` +
                      `commit them and then update ${bundle} with 'npm run build'`);
    }

  } catch (e) {
    console.log(e.message);
    if (force) console.log(`force=true, continuing with existing ${bundle}`);
    else throw new Error(`or run 'npm run deploy -- --force=true' to use existing ${bundle}`);
  }

  return args;
}

module.exports = { boolArg, checkDeploy };
