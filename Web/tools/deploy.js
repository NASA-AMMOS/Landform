const { spawn, checkDeploy, prompt } = require('./toolUtil');
const defEnv = require('../config').app.deployEnvironment;

//npm run deploy -- [environment-name] [-f|--force|--force=true] [--profile=foo]

checkDeploy('deploy')
  .then(async(args) => {
    //eb deploy <environment-name>
    //
    //normally when run from a git repo with no .ebignore file, as we are, this command will use git archive
    //to make a zip of the most recent HEAD and deploy that
    //
    //however, because we set deploy.artifact in .elasticbeanstalk/config.yml
    //that zip file will actually be deployed
    //
    //https://docs.aws.amazon.com/elasticbeanstalk/latest/dg/eb-cli3-configuration.html#eb-cli3-artifact

    if (!args.length || args[0].startsWith('-')) args.unshift(defEnv);

    const env = args[0];

    args.unshift('-v');
    args.unshift('deploy');

    if (await prompt('deploy', `deploy to environment '${env}'`)) spawn('eb', args);
    else console.log('aborted');
  })
  .catch(e => console.error(e.message));
