const child = require('child_process');
const fs = require('fs-extra');
const readline = require('readline');
const parseJson = require('json-parse-better-errors');
const stripJsonComments = require('strip-json-comments');

const bundle = require('../config').app.bundle;

//checks if v is "--name", "--name=true", or "-n"
function boolArg(v, name) {
  const lv = v.toLowerCase(), ln = name.toLowerCase();
  return lv === `-${ln[0]}` || lv === `--${ln}` || lv === `--${ln}=true`;
}

function hasFlag(name) { return process.argv.slice(2).some(v => boolArg(v, name)); }

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

//verifies that the app bundle zip exists and looks ok
//if not, throws Error
//respects the "force" command line options (--force, --force=true, -f)
//if any of those options are present then the only fatal error is if the bundle doesn't exist
//returns command line args with force options removed
async function checkDeploy(cmd) {

  let force = false;
  //argv[0] is the path to the node binary
  //argv[1] is the path to start script
  const args = process.argv.slice(2).reduce((a, v) => {
    if (boolArg(v, 'force')) force = true; else a.push(v);
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
    else throw new Error(`or run 'npm run ${cmd} -- --force=true' to use existing ${bundle}`);
  }

  return args;
}

//check that we are running under a compatible terminal emulator
//in particular Cygwin bash prompt is not a real terminal emulator
//and git bash requires prefixing the command line with "winpty"
async function checkTTY(cmd) {

  let uname = '';
  try { uname = exec('uname -s').toLowerCase(); } catch (e) {}

  if (uname.startsWith('cygwin')) {

    console.log('this command is not compatibile with Cygwin prompt, use Windows cmd');
    console.log(`or run 'winpty node tools/${cmd}.js [args]' in git bash`);
    return false;

  } else if (uname.startsWith('mingw')) {

    if (process.stdout.isTTY) return true;
    console.log(`this command requires a TTY: run 'winpty node tools/${cmd}.js [args]' in git bash or use Windows cmd`);
    return false;
  }

  //not cygwin or mingw (git bash), so probably Windows cmd or linux
  return process.stdout.isTTY;
}

//prompt user with a yes/no question
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

function parseSec(duration) {
  if (!duration || duration.length < 1) return 0;
  if (duration === 'help') return 'N|Ns|Nm|Nh|Nd|Nw|No|Ny';
  const suffix = duration[duration.length - 1].toLowerCase();
  let sec = parseInt(duration);
  switch (suffix) {
    case 's': default: break;
    case 'm': sec *= 60; break;
    case 'h': sec *= 60 * 60; break;
    case 'd': sec *= 60 * 60 * 24; break;
    case 'w': sec *= 60 * 60 * 24 * 7; break;
    case 'o': sec *= 60 * 60 * 24 * 7 * 31; break;
    case 'y': sec *= 60 * 60 * 24 * 7 * 365; break;
  }
  return sec;
}


//yyyyMMddHHmmssZzz
function timeStamp(timeOrDate) {
  const d = (timeOrDate instanceof Date) ? timeOrDate : timeOrDate > 0 ? new Date(timeOrDate) : new Date();
  const tzo = -d.getTimezoneOffset();
  const pad2 = n => { n = Math.floor(Math.abs(n)); return (n < 10 ? '0' : '') + n; };
  return `${d.getFullYear()}${pad2(d.getMonth() + 1)}${pad2(d.getDate())}` +
         `${pad2(d.getHours())}${pad2(d.getMinutes())}${pad2(d.getSeconds())}` +
         `Z${tzo >= 0 ? '+' : '-'}${pad2(tzo / 60)}`;
}

function timeFmt(ms) {
  if (ms <= 0) return '0s';
  const msInS = 1000, msInM = 60 * msInS, msInH = 60 * msInM, msInD = 24 * msInH, msInY = 365 * msInD;
  const y = Math.floor(ms / msInY); ms -= y * msInY; const yy = y > 0 ? `${y}y ` : '';
  const d = Math.floor(ms / msInD); ms -= d * msInD; const dd = d > 0 ? `${d}d ` : '';
  const h = Math.floor(ms / msInH); ms -= h * msInH; const hh = h > 0 ? `${h}h ` : '';
  const m = Math.floor(ms / msInM); ms -= m * msInM; const mm = m > 0 ? `${m}m ` : '';
  const s = Math.floor(ms / msInS); ms -= s * msInS; const ss = s > 0 || ms > 0 ? `${s}.${ms}s` : '';
  return `${yy}${dd}${hh}${mm}${ss}`;
}

function deepCopy(obj) { return JSON.parse(JSON.stringify(obj)); }

//read json with comments and better error reporting
function readJson(file) { return parseJson(stripJsonComments(fs.readFileSync(file, 'utf8'))); }

module.exports = {
  boolArg, hasFlag,
  exec, spawn, spawnSync,
  checkDeploy, checkTTY, prompt,
  parseSec, timeStamp, timeFmt,
  deepCopy,
  readJson,
};
