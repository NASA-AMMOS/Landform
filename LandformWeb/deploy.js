const spawn = require('child_process').spawn;

const { checkDeploy } = require('./deployUtil');

//npm run deploy -- [-f|--force|--force=true] [--profile=foo]

checkDeploy()
  .then((args) => {
    const cmd = 'eb';
    args = ['deploy', 'landformweb', ...args];
    console.log(`running '${cmd} ${args.join(' ')}'`);
    spawn(cmd, args, { stdio: 'inherit', shell: true });
  })
  .catch(e => console.error(e.message));
