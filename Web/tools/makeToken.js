const { makeToken } = require('../token');

const argv = process.argv;
function usage() {
  console.log('USAGE: makeToken user N|Ns|Nm|Nh|Nd|Nw|No|Ny [-h|--help]');
  process.exit();
}

if (argv.length < 4 || argv.some(a => a.toLowerCase() === '-h' || a.toLowerCase() === '--help')) usage();

const user = argv[2];

const duration = argv[3];
if (duration.length < 1) usage();
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

console.log(`token for user '${user}' with duration ${duration} (${sec}s):`);

console.log(makeToken(user, sec));
