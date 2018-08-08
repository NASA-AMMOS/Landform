const execSync = require('child_process').execSync;
const fs = require('fs-extra');

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

  const zf = 'landformweb.zip';
  if (!fs.pathExistsSync(zf)) throw new Error(`${zf} not found, run 'npm run build'`);

  try {

    const ht = new Date(1000 * await execSync('git show -s --format="%ct" HEAD'));
    const zt = fs.statSync(zf).mtime;
    if (ht > zt) {
      throw new Error(`HEAD newer than ${zf}: ${ht.toLocaleString()} > ${zt.toLocaleString()}\n` +
                      `update ${zf} with 'npm run build'`);
    }

    const modified = await execSync('git diff-index --name-status HEAD --');
    if (modified) {
      throw new Error(`uncommitted changes:\n${modified}\n` +
                      `commit them and then update ${zf} with 'npm run build'`);
    }

  } catch (e) {
    console.log(e.message);
    if (force) console.log(`force=true, continuing with existing ${zf}`);
    else throw new Error(`or run 'npm run deploy -- --force=true' to use existing ${zf}`);
  }

  return args;
}

module.exports = { boolArg, checkDeploy };
