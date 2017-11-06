
#### Working with a Cloud Formation stack: 

####Stack creation and deploying 
#####To create your stack: 

You need: an AWS account with creation permissions for all stack resources, a pem file for accessing, the AWS CLI installed 

From the CLI or console, create a new stack using pipeline.template. You must specify a username, which will be a tag for your CloudWatch metrics, the name of your key file, and a unique prefix for table names. 

Stack creation does not set up any mappings between S3 and the stack. Configure S3 bukets to send notifications to S3 listener lambdas for the prefixes where observations/tiles/whatever will go. 

#####Deploying code to worker instances: 

Use the deploylambdas and deploylandform scripts to deploy to your workers and lambdas. The workers do not (currently) have an initial deployment, so you have to deploy at least once before they will do anything.

#####Deploying frontend code: 

See the pipeline-frontend repo for info on deploying to the EB instance.  

#### Some failures: 
During deployment: 

+ For the error `The deployment failed because an invalid version value () was entered in the application specification file. Make sure your AppSpec file specifies "0.0" as the version, and then try again.`:
The appspec yaml file may not be saving with the correct encoding in Visual Studio. In Advanced Save Options, ensure that the yaml is saved as UTF-8 *without* encodings. 
+ For "Found a tab character" errors: check yaml files in an online checker. 
+ Code deploy not running your powershell hooks? They have to be in the root folder of the deployment (the same level as appspec.yml)
+ Failures of powershell scripts do not cause deployments to fail. This is a bug, there is an open support request to Amazon about it. (Support case id 4564068771)

Running worker code on your own machine: 
Workers just run the Landform command. Tiling workers run queuelisten, Alignment workers run alignmentworker. To run on your machine, you need a config file with the resource names specified in the Config type at the top of those files. 

#### Structural overview: 

##### Mesh tiling pipeline: 

LambdaS3TileIntake is notified on PUTs to buckets. (Notifications are not configured by cloudformation. They will eventually be configured by API, for now configure by hand.) It uploads tile metadata to DynamoDB. 
Each parent tile has an entry in DynamoDB. TileIntake records a child tile in the entry for its parent, adding an entry for the parent tile if nececary. 

DynamoDB generates a stream of changes to the tile metadata table. This stream is processed by LambdaDynamoProcessing, which puts a parent generation job in the job queue when it recieves a stream record 
where all children of a parent tile are present. 

Workers poll the job queue for jobs. When one arrives, the worker downloads the child tiles from S3, generates a parent tile, and uploads the parent tile. If successful, it deletes the message from the job queue. 

LambdaQueueSizeMetric publishes a custom CloudWatch metric describing the size of the JobQueue every minute. (This lambda code is shared between the two pipelines, but each pipeline has its own lambda publishing to a different metric.) 
An alarm on that custom metric triggers autoscaling of the worker group when the alarm is above a given level. 
TODO this autoscaling (rate, trigger level, etc) can definitely be optimized. 

##### Alignment pipeline: 

No physical resources are shared between the mesh tiling and alignment pipeline, although the two share the worker code (though use a different command) and share the LambdaQueueSizeMetric code. 

Jobs are added to the alignment job queue by either LambdaS3ImageIntake (configured like TileIntake) or by LambdaScanS3, which is a one-time job that scans through an existing S3 bucket or folder and adds the files it finds to the queue. 

The queue contians three types of jobs: *Image intake* jobs, *find overlaps* jobs, and *match images* jobs. Job type is specified in the message attributes, along with any info needed for the worker to complete the job. 

Workers poll the job queue for any type of job, then call the appropriate function to complete the job. If successful, they start any new jobs and delete the message. 

Workers upload intermediate data to S3 as needed. 

A QueueSizeMetric lambda creates a custom metric enabling autoscaling, as in the mesh tiling pipeline. 