const child = require('child_process');
const fs = require('fs-extra');
const readline = require('readline');

const bundle = require('./config').app.bundle;

function boolArg(v, name) {
  const lv = v.toLowerCase(), ln = name.toLowerCase();
  return lv === `-${ln[0]}` || lv === `--${ln}` || lv === `--${ln}=true`;
}

//run cmd, wait for it to complete, and return its stdout spew
//throws if cmd not found or returns nonzero exit status
//ignores stderr from cmd
function exec(cmd) { return child.execSync(cmd, { stdio: ['ignore', 'pipe', 'ignore'], encoding: 'utf8' }); }

//spawn cmd under a shell
function spawn(cmd, args, opts) {
  opts = opts || {};
  if (!('stdio' in opts)) opts.stdio = 'inherit';
  if (!('shell' in opts)) opts.shell = true;
  console.log(`running '${cmd} ${args.join(' ')}'`);
  return child.spawn(cmd, args, opts);
}

//spawn cmd under a shell and wait for it to complete
function spawnSync(cmd, args, opts) {
  opts = opts || {};
  if (!('stdio' in opts)) opts.stdio = 'inherit';
  if (!('shell' in opts)) opts.shell = true;
  console.log(`running '${cmd} ${args.join(' ')}'`);
  return child.spawnSync(cmd, args, opts);
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

    const ht = new Date(1000 * exec('git show -s --format="%ct" HEAD'));
    const bt = fs.statSync(bundle).mtime;
    if (ht > bt) {
      throw new Error(`HEAD newer than ${bundle}: ${ht.toLocaleString()} > ${bt.toLocaleString()}\n` +
                      `update ${bundle} with 'npm run build'`);
    }

    const modified = exec('git diff-index --name-status HEAD --');
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

async function checkTTY(cmd) {

  let uname = '';
  try { uname = exec('uname -s').toLowerCase(); } catch (e) {}

  if (uname.startsWith('cygwin')) {

    console.log('this command is not compatibile with Cygwin prompt, use Windows cmd');
    console.log(`or run 'winpty node ${cmd}.js [args]' in git bash`);
    return false;

  } else if (uname.startsWith('mingw')) {

    if (process.stdout.isTTY) return true;
    console.log(`run 'winpty node ${cmd}.js [args]' in git bash, or use Windows cmd`);
    return false;
  }

  //not cygwin or mingw (git bash), so probably Windows cmd or linux
  return process.stdout.isTTY;
}

async function prompt(cmd, msg) {

  if (!(await checkTTY(cmd))) return false;

  const rl = readline.createInterface({ input: process.stdin, output: process.stdout });
  return new Promise((accept) => {
    rl.question(`${msg} (y/n)? `, (answer) => {
      rl.close();
      accept(answer && answer.toLowerCase()[0] === 'y');
    });
  });
}

module.exports = { boolArg, exec, spawn, spawnSync, checkDeploy, checkTTY, prompt };
