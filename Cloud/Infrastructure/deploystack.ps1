######################################################
## Deploy the stack in console.template, which has as sub-stacks alignment.template and tiling.template 
## If you use the name of an existing stack, the stack will be updated instead. Only the resources that need to change 
##      (based on changes in parameters or changes in any template used by the stack) will change. 
## Helpful hint: changing the table prefix will re-create the tables, effectively emptying them. 
## Helpful hint #2: If you make changes that update the workers' launch configuration, you will have to mark as unhealthy all workers that were started before the update
##
## UPDATE THIS SCRIPT WHENEVER...
## A lambda function is added
## A stack parameter is added 
######################################################

#gather stack-specific parameters from user 
param([Parameter(Mandatory=$true)][System.String]$StackName,
    [Parameter(Mandatory=$true, HelpMessage="Path within bucket to folder for this stack. Does not include bucket, leading /, or trailing /")][System.String]$S3Prefix,
    [Parameter(Mandatory=$true, HelpMessage="Bucket for this stack's resources and default mappings")][System.String]$Bucket,
    [Parameter(Mandatory=$true, HelpMessage="Name of key pair to log into EC3 workers.")][System.String]$KeyPair,
    [Parameter(Mandatory=$true, HelpMessage="Prefix for dynamo tables for alignment stack.")][System.String]$AlignmentTablePrefix,
    [Parameter(Mandatory=$true, HelpMessage="Prefix for dynamo tables for tiling stack.")][System.String]$TilingTablePrefix,
    [Parameter(Mandatory=$false, HelpMessage="Name of the IAM role to be assumed by workers. If different workers (frontend vs alignment vs mesh tiling) should have different roles, just change this script to provide them. Not required, default=landlords")][System.String]$WorkerRole="landlords",
    [Parameter(Mandatory=$false, HelpMessage="AMI to use. Should be default Amazon-provided windows AMI, default=ami-32320452")][System.String]$AMIId="ami-32320452")

#upload resources to a resource folder within the user-specified S3 prefix 
aws s3 cp --recursive StackResources s3://$Bucket/$S3Prefix/stack_resources

#build lambdas and upload deployed code to resource folder so they are availible for stack
Write-Host "...Building and uploading initial lambda code"
cd ..\..\LambdaS3ImageIntake 
dotnet restore 
dotnet build
aws s3 cp bin\Release\netcoreapp1.0\LambdaS3ImageIntake.zip s3://$Bucket/$S3Prefix/stack_resources/LambdaS3ImageIntake.zip

cd ..\LambdaQueueSizeMetric
dotnet restore 
dotnet build
aws s3 cp bin\Release\netcoreapp1.0\LambdaQueueSizeMetric.zip s3://$Bucket/$S3Prefix/stack_resources/LambdaQueueSizeMetric.zip

cd ..\LambdaScanS3
dotnet restore 
dotnet build
aws s3 cp bin\Release\netcoreapp1.0\LambdaScanS3.zip s3://$Bucket/$S3Prefix/stack_resources/LambdaScanS3.zip

cd ..\LambdaDynamoProcessing
dotnet restore 
dotnet build
aws s3 cp bin\Release\netcoreapp1.0\LambdaDynamoProcessing.zip s3://$Bucket/$S3Prefix/stack_resources/LambdaDynamoProcessing.zip

cd ..\LambdaS3TileIntake
dotnet restore 
dotnet build
aws s3 cp bin\Release\netcoreapp1.0\LambdaS3TileIntake.zip s3://$Bucket/$S3Prefix/stack_resources/LambdaS3TileIntake.zip

cd ..\Cloud\Infrastructure

#start the stack. Note: relies on names and file strcutre within StackResources. 
aws cloudformation deploy --template-file StackResources/console.template --stack-name $StackName --parameter-overrides BucketName=$Bucket UserKeyName=$KeyPair AlignmentTemplate=stack_resources/alignment.template TilingTemplate=stack_resources/tiling.template S3Prefix=$S3Prefix AmiId=$AMIId CppDistResource=$S3Prefix/stack_resources/vc_redist.x64.exe CppDistResource2013=$S3Prefix/stack_resources/vcredist_x64_2013.exe QueueMonitorLambda=$S3Prefix/stack_resources/LambdaQueueSizeMetric.zip ImageIntakeLambda=$S3Prefix/stack_resources/LambdaS3ImageIntake.zip ScanS3Lambda=$S3Prefix/stack_resources/LambdaScanS3.zip TileDynamoProcessingLambda=$S3Prefix/stack_resources/LambdaDynamoProcessing.zip S3TileIntakeLambda=$S3Prefix/stack_resources/LambdaS3TileIntake.zip FrontendInstanceProfile=$WorkerRole AlignmentTablePrefix=$AlignmentTablePrefix TilingTablePrefix=$TilingTablePrefix AlignmentWorkerInstanceProfile=$WorkerRole TilingWorkerInstanceProfile=$WorkerRole 

#Create initial mappings from S3 prefixes -> intake lambdas. Stack users can add more as they wish

<# Turns out this is hard
#Get alignment stack name
$json = aws cloudformation describe-stack-resources --stack-name $StackName --logical-resource-id Alignment --output json 
$AlignmentStack = ($json | ConvertFrom-Json).StackResources.PhysicalResourceId
#Get image intake lambda name 
$json = aws cloudformation describe-stack-resources --stack-name $AlignmentStack --logical-resource-id S3ImageIntake --output json 
$ImageIntakeLambda = ($json | ConvertFrom-Json).StackResources.PhysicalResourceId
$json = aws lambda get-function --function-name $ImageIntakeLambda --output json
$ImageIntakeLambdaArn = ($json | ConvertFrom-Json).Configuration.FunctionArn

#Create mapping 
$json = @"
    {\"LambdaFunctionConfigurations\": [{\"Id\": \"string\",\"LambdaFunctionArn\": \"$ImageIntakeLambdaArn\",\"Events\": [\"s3:ObjectCreated:*\"],\"Filter\": {\"Key\": {\"FilterRules\": [{\"Name\": \"prefix\",\"Value\": \"$S3Prefix/images\"}]}}}]}
"@
aws s3api put-bucket-notification-configuration --bucket $Bucket --notification-configuration $json
#create the label in S3 (not nececary, but makes it more obvious where to upload things) 
aws s3api put-object --bucket $Bucket --key $S3Prefix/images/

Write-Host "S3 -> lambda mapping created: Put images in s3://$Bucket/$S3Prefix/images to have them processed by the alignment pipeline"
#>
