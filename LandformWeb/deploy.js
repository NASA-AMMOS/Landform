const spawn = require('child_process').spawn;

const { checkDeploy } = require('./deployUtil');

checkDeploy()
  .then((args) => {
    //npm run deploy -- --profile=foo
    //=> eb deploy landformweb --profile foo
    const cmd = 'eb';
    args = ['deploy', 'landformweb', ...args];
    console.log(`running '${cmd} ${args.join(' ')}'`);
    spawn(cmd, args, { stdio: 'inherit', shell: true });
  })
  .catch(e => console.error(e.message));
