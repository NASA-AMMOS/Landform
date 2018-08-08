const execSync = require('child_process').execSync;
const fs = require('fs-extra');

async function checkDeploy() {

  let force = false;
  const args = process.argv.slice(2).reduce((a, v) => {
    const lv = v.toLowerCase();
    if (lv === '-f' || lv === '--force' || lv === '--force=true') force = true;
    else a.push(v);
    return a;
  }, []);

  const zf = 'landformweb.zip';
  if (!fs.pathExistsSync(zf)) throw new Error(`${zf} not found, run 'npm run build'`);

  try {

    const hf = '../.git/HEAD';
    const ht = fs.statSync(hf).mtime;
    const zt = fs.statSync(zf).mtime;
    if (ht > zt) {
      throw new Error(`${hf} newer than ${zf}: ${ht.toLocaleString()} > ${zt.toLocaleString()}\n` +
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

module.exports = { checkDeploy };
