
#### Working with a Cloud Formation stack: 

#####The basics: 
+ Dev bucket: landlords-dev 


#####To create your stack: 

You need: an AWS account with creation permissions for all stack resources, a pem file for accessing 

From the CLI or console, create a new stack using pipeline.template. You must specify a username, which will be a tag for your CloudWatch metrics, and the name of your key file. 

Stack creation does not set up any mappings between S3 and the stack. 

*TO IMPLEMENT* The stack by default gets code for lambda functions and the worker application from *repo*. If you want to point at your own copy of the build, specify its S3 address in the parameters of your stack. 

Cloud Formation cannot update existing resources, so you need to add a notification configuration to the bucket where your stack will recieve data. 

#####Deploying code to worker instances : 

Use the deploylambdas and deploylandform scripts to deploy to your workers and lambdas. 

#####Deploying frontend code : 

Deploying the stack will create a frontend that runs a node.js app. 

#### Some failures: 
During deployment: 
+For the error `The deployment failed because an invalid version value () was entered in the application specification file. Make sure your AppSpec file specifies "0.0" as the version, and then try again.`:
The appspec yaml file may not be saving with the correct encoding in Visual Studio. In Advanced Save Options, ensure that the yaml is saved as UTF-8 *without* encodings. 
+ For "Found a tab character" errors: check yaml files in an online checker. 
+ Code deploy not running your powershell hooks? As of 10/2017, they have to be in the root folder of the deployment (the same level as appspec.yml)
+ 