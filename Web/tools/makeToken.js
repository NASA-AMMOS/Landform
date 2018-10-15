const { makeToken } = require('../token');
const { hasFlag, parseSec } = require('./toolUtil');

const argv = process.argv;

function usage() {
  console.log(`USAGE: makeToken user ${parseSec('help')} [-h|--help]`);
  process.exit();
}

if (argv.length < 4 || hasFlag('help')) usage();

const user = argv[2];

const duration = argv[3];
if (duration.length < 1) usage();
const sec = parseSec(duration);

console.log(`token for user '${user}' with duration ${duration} (${sec}s):`);

console.log(makeToken(user, sec));
