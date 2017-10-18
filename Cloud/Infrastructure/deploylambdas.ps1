################
## Build and deploy DynamoProcessing, QueueSizeMetric, S3PutProcessing 
## Additional deployment configuration stored in aws-lambda-tools-defaults.json for each lambda project 
## Note: Lambda env vars concerning stack configuration (Dynamo table name, job queue url, etc) are set by cloudformation template. 
##       Adding additional env vars here will overwrite those variables, so you will need to set them as well. 
################

#name of stack 
param([Parameter(Mandatory=$true)][System.String]$StackName)

#logical id for each lambda within cloud formation stack
$DynamoProcessingResource = "MetadataTriggerProcessing"
$QueueSizeMetricResource = "QueueMonitor"
$S3PutProcessingResource = "S3Watcher"


### Get names for each of our resources 
$json = aws cloudformation describe-stack-resources --stack-name $StackName --logical-resource-id $QueueSizeMetricResource --output json 
$QueueSizeMetricName = ($json | ConvertFrom-Json).StackResources.PhysicalResourceId

$json = aws cloudformation describe-stack-resources --stack-name $StackName --logical-resource-id $DynamoProcessingResource --output json 
$DynamoProcessingName = ($json | ConvertFrom-Json).StackResources.PhysicalResourceId

$json = aws cloudformation describe-stack-resources --stack-name $StackName --logical-resource-id $S3PutProcessingResource --output json 
$S3PutProcessingName = ($json | ConvertFrom-Json).StackResources.PhysicalResourceId


### Build and upload lambdas to the physical resources we found above 

cd ..\..\LambdaDynamoProcessing 
dotnet restore 
dotnet lambda deploy-function $DynamoProcessingName 

cd ..\LambdaQueueSizeMetric
dotnet restore 
dotnet lambda deploy-function $QueueSizeMetricName 

cd ..\LambdaS3PutProcessing
dotnet restore
dotnet lambda deploy-function $S3PutProcessingName

cd ..