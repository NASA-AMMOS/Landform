################
## Build and deploy QueueSizeMetric, ImageIntake lambdas
## Additional deployment configuration stored in aws-lambda-tools-defaults.json for each lambda project 
## Note: Lambda env vars concerning stack configuration (Dynamo table name, job queue url, etc) are set by cloudformation template. 
##       If you add additional env vars here, you will overwrite those vars. 
################

#name of stack 
param([Parameter(Mandatory=$true)][System.String]$StackName)

#logical id for each lambda within cloud formation stack
$S3ImageIntakeResource = "S3ImageIntake"
$QueueSizeMetricResource = "QueueMonitor"
$S3ScanResource = "S3Scan"

### Get names for each of our resources 
$json = aws cloudformation describe-stack-resources --stack-name $StackName --logical-resource-id $QueueSizeMetricResource --output json 
$QueueSizeMetricName = ($json | ConvertFrom-Json).StackResources.PhysicalResourceId

$json = aws cloudformation describe-stack-resources --stack-name $StackName --logical-resource-id $S3ImageIntakeResource --output json 
$S3ImageIntakeName = ($json | ConvertFrom-Json).StackResources.PhysicalResourceId

$json = aws cloudformation describe-stack-resources --stack-name $StackName --logical-resource-id $S3ScanResource --output json 
$S3ScanName = ($json | ConvertFrom-Json).StackResources.PhysicalResourceId


### Build and upload lambdas to the physical resources we found above 

cd ..\..\LambdaS3ImageIntake 
dotnet restore 
dotnet lambda deploy-function $S3ImageIntakeName 

cd ..\LambdaQueueSizeMetric
dotnet restore 
dotnet lambda deploy-function $QueueSizeMetricName 

cd ..\LambdaScanS3
dotnet restore 
dotnet lambda deploy-function $S3ScanName 

cd ..