
#### Working with a Cloud Formation stack: 

####Stack creation and deploying 
#####To create your stack: 

You need: an AWS account with creation permissions for all stack resources, a pem file for accessing, the AWS CLI installed 

From the CLI or console, create a new stack using pipeline.template. You must specify a username, which will be a tag for your CloudWatch metrics, the name of your key file, and a unique prefix for table names. 

Stack creation does not set up any mappings between S3 and the stack. Configure S3 bukets to send notifications to S3 listener lambdas for the prefixes where observations/tiles/whatever will go. 

#####Deploying code to worker instances : 

Use the deploylambdas and deploylandform scripts to deploy to your workers and lambdas. The workers do not (currently) have an initial deployment, so you have to deploy at least once before they will do anything.

#####Deploying frontend code : 

See the pipeline-frontend repo for info on deploying to the EB instance.  

#### Some failures: 
During deployment: 
+For the error `The deployment failed because an invalid version value () was entered in the application specification file. Make sure your AppSpec file specifies "0.0" as the version, and then try again.`:
The appspec yaml file may not be saving with the correct encoding in Visual Studio. In Advanced Save Options, ensure that the yaml is saved as UTF-8 *without* encodings. 
+ For "Found a tab character" errors: check yaml files in an online checker. 
+ Code deploy not running your powershell hooks? They have to be in the root folder of the deployment (the same level as appspec.yml)
+ Failures of powershell scripts do not cause deployments to fail. This is a bug, there is an open support request to Amazon about it. (Support case id 4564068771)

Running worker code on your own machine: 
Workers just run the Landform command. Tiling workers run queuelisten, Alignment workers run alignmentworker. To run on your machine, you need a config file with the resource names specified in the Config type at the top of those files. 

#### Structural overview: 
