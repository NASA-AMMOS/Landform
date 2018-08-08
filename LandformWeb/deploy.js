const spawn = require('child_process').spawn;

const defEnv = require('./config').app.ebDeployEnvironment;
const { checkDeploy } = require('./deployUtil');

//npm run deploy -- [environment-name] [-f|--force|--force=true] [--profile=foo]

checkDeploy()
  .then((args) => {
    const cmd = 'eb';

    //eb deploy <environment-name>
    //
    //normally when run from a git repo with no .ebignore file, as we are, this command will use git archive
    //to make a zip of the most recent HEAD and deploy that
    //
    //however, becasue we set deploy.artifact in .elasticbeanstalk/config.yml
    //that zip file will actually be deployed
    //
    //https://docs.aws.amazon.com/elasticbeanstalk/latest/dg/eb-cli3-configuration.html#eb-cli3-artifact

    if (!args.length || args[0].startsWith('-')) args.unshift(defEnv);

    args.unshift('deploy');

    console.log(`running '${cmd} ${args.join(' ')}'`);
    spawn(cmd, args, { stdio: 'inherit', shell: true });
  })
  .catch(e => console.error(e.message));
