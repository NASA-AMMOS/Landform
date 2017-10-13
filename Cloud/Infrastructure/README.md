
#### Working with a Cloud Formation stack: 

#####The basics: 
+ Dev bucket: landlords-dev 


#####To create your stack: 

*TODO someone - dev or script - has to create account, pem file, perms for the dev*

From the CLI or console, create a new stack using pipeline.template. You must specify a username, which will be a tag for your CloudWatch metrics, and the name of your key file. 

*TO IMPLEMENT* The stack by default gets code for lambda functions and the worker application from *repo*. If you want to point at your own copy of the build, specify its S3 address in the parameters of your stack. 

Cloud Formation cannot update existing resources, so you need to add a notification configuration to the bucket where your stack will recieve data. 

#####To make your workers point at a build of your choice: 

Use Cloud Formation's stack update (from the CLI, aws `cloudformation update-stack`, or from the Console) to change the stack's input parameter from the default zipped package to your own zipped build in S3. Cloud Formation will replace only the 

#####To change the application version used by workers: 

1. Upload a zip file of your build to the S3 location your stack points to. 
2. Mark as unhealthy any workers currently running in your stack. From the CLI: `aws autoscaling set-instance-health --instance-id your-instance-id --health-status Unhealthy`

#####To change the function version used by lambdas: 
+ Use the AWS Visual Studio plug-in to upload your code (right click on the lambda project, then enter your lambda's name)
+ OR build your code and upload a zip file of the binaries to the lambda 

(Unlike EC2 instances, lambda instances do not read their source on creation from the S3 location you pointed to in your Cloud Formation parameters, so replacing this zip file is not enough to update your lambda)

#### Some failures: 
During deployment: 
+For the error `The deployment failed because an invalid version value () was entered in the application specification file. Make sure your AppSpec file specifies "0.0" as the version, and then try again.`:
The appspec yaml file may not be saving with the correct encoding in Visual Studio. In Advanced Save Options, ensure that the yaml is saved as UTF-8 *without* encodings. 
+ For "Found a tab character" errors: check yaml files in an online checker. 
+ Code deploy not running your powershell hooks? As of 10/2017, they have to be in the root folder of the deployment (the same level as appspec.yml)
+ 