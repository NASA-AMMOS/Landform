const defEnv = require('./config').app.ebDeployEnvironment;
const { spawn, checkDeploy, prompt } = require('./deployUtil');

//npm run deploy -- [environment-name] [-f|--force|--force=true] [--profile=foo]

checkDeploy()
  .then(async(args) => {
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

    const env = args[0];

    args.unshift('-v');
    args.unshift('deploy');

    if (await prompt('deploy', `deploy to environment '${env}'`)) spawn(cmd, args);
    else console.log('aborted');
  })
  .catch(e => console.error(e.message));
