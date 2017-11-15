################
## Build and deploy TileIntake, ImageIntake, S3Scan, DynamoProcessing, QueueSizeMetric, S3PutProcessing lambdas
## Additional deployment configuration stored in aws-lambda-tools-defaults.json for each lambda project 
## Note: Lambda env vars concerning stack configuration (Dynamo table name, job queue url, etc) are set by cloudformation template. 
##       If you add additional env vars here, you will overwrite those vars. 
##
## UPDATE THIS SCRIPT WHENEVER: 
## A new lambda is added to an existing template 
## A new nested template with lambdas is added
################

#name of stack 
param([Parameter(Mandatory=$true)][System.String]$StackName)

# Get upload location (bucket and prefix) from stack. 
$json = aws cloudformation describe-stacks --stack-name $StackName --query 'Stacks[0].Parameters[?ParameterKey==`BucketName`]' --output json 
$UploadBucket = ($json | ConvertFrom-Json).ParameterValue
$json = aws cloudformation describe-stacks --stack-name $StackName --query 'Stacks[0].Parameters[?ParameterKey==`S3Prefix`]' --output json 
$S3Prefix = ($json | ConvertFrom-Json).ParameterValue

#get names of nested stacks
$json = aws cloudformation describe-stack-resources --stack-name $StackName --logical-resource-id Alignment --output json 
$AlignmentStackName = ($json | ConvertFrom-Json).StackResources.PhysicalResourceId
$json = aws cloudformation describe-stack-resources --stack-name $StackName --logical-resource-id Tiling --output json 
$TilingStackName = ($json | ConvertFrom-Json).StackResources.PhysicalResourceId

#logical id for each lambda within cloud formation stack
$S3ImageIntakeResource = "S3ImageIntake"
$QueueSizeMetricResource = "QueueMonitor"
$S3ScanResource = "S3Scan"
$S3TileIntakeResource = "S3Watcher"
$DynamoProcessingResource = "MetadataTriggerProcessing"

### Get names for each of our tiling resources 
$json = aws cloudformation describe-stack-resources --stack-name $TilingStackName --logical-resource-id $QueueSizeMetricResource --output json 
$TilingQueueSizeMetricName = ($json | ConvertFrom-Json).StackResources.PhysicalResourceId

$json = aws cloudformation describe-stack-resources --stack-name $TilingStackName --logical-resource-id $DynamoProcessingResource --output json 
$DynamoProcessingName = ($json | ConvertFrom-Json).StackResources.PhysicalResourceId

$json = aws cloudformation describe-stack-resources --stack-name $TilingStackName --logical-resource-id $S3TileIntakeResource --output json 
$S3PutProcessingName = ($json | ConvertFrom-Json).StackResources.PhysicalResourceId



### Get names for each of our resources 
$json = aws cloudformation describe-stack-resources --stack-name $AlignmentStackName --logical-resource-id $QueueSizeMetricResource --output json 
$AlignmentQueueSizeMetricName = ($json | ConvertFrom-Json).StackResources.PhysicalResourceId

$json = aws cloudformation describe-stack-resources --stack-name $AlignmentStackName --logical-resource-id $S3ImageIntakeResource --output json 
$S3ImageIntakeName = ($json | ConvertFrom-Json).StackResources.PhysicalResourceId

$json = aws cloudformation describe-stack-resources --stack-name $AlignmentStackName --logical-resource-id $S3ScanResource --output json 
$S3ScanName = ($json | ConvertFrom-Json).StackResources.PhysicalResourceId


### Build and upload lambdas to the tiling physical resources we found above 

cd ..\..\LambdaDynamoProcessing 
dotnet restore 
dotnet lambda deploy-function $DynamoProcessingName 
aws s3 cp bin\Release\netcoreapp1.0\LambdaDynamoProcessing.zip s3://$UploadBucket/$S3Prefix/pipeline_resources/LambdaDynamoProcessing.zip

cd ..\LambdaS3TileIntake
dotnet restore
dotnet lambda deploy-function $S3PutProcessingName
aws s3 cp bin\Release\netcoreapp1.0\LambdaS3TileIntake.zip s3://$UploadBucket/$S3Prefix/pipeline_resources/LambdaS3TileIntake.zip

### Build and upload lambdas to the alignment physical resources we found above 

cd ..\LambdaS3ImageIntake 
dotnet restore 
dotnet lambda deploy-function $S3ImageIntakeName 
aws s3 cp bin\Release\netcoreapp1.0\LambdaS3ImageIntake.zip s3://$UploadBucket/$S3Prefix/pipeline_resources/LambdaS3ImageIntake.zip

cd ..\LambdaScanS3
dotnet restore 
dotnet lambda deploy-function $S3ScanName 
aws s3 cp bin\Release\netcoreapp1.0\LambdaScanS3.zip s3://$UploadBucket/$S3Prefix/pipeline_resources/LambdaScanS3.zip

## Queue size metric lambda is the same for both alignment and tiling 
cd ..\LambdaQueueSizeMetric
dotnet restore 
dotnet lambda deploy-function $TilingQueueSizeMetricName 
dotnet lambda deploy-function $AlignmentQueueSizeMetricName 
aws s3 cp bin\Release\netcoreapp1.0\LambdaQueueSizeMetric.zip s3://$UploadBucket/$S3Prefix/pipeline_resources/LambdaQueueSizeMetric.zip

cd ..