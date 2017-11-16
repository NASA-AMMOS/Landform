###############################
## Deploys current release build of landform to Application/Deployment Group for user-specified stack
##      - Finds aws resources for deployment to this stack (bucket name, s3 prefix, code deploy resources in nested stacks)
##      - Constructs a release folder (CodeDeploy metadata and most recent Landform release build) 
##      - Deploys that release folder using CodeDeploy
## 
## Add additional worker applications by: adding additional nested stacks and appspec.yaml files, then adding those as needed to this file. 
## 
## Note: If you have RPCed into a worker and started landform yourself, deployment will *fail*. 
##       Either terminate that instance or manually kill the Landform you started
## Note: This could be easily modified to deploy to both mesh and alignment workers, instead of only one at a time
##
## UPDATE THIS SCRIPT WHENEVER: 
## A new nested stack requiring deployment to workers is added 
###############################

#name of stack 
#and
#applicaiton type. options are mesh (runs queuelisten) | align (runs alignmentworker) 
param([Parameter(Mandatory=$true)][System.String]$StackName,
    [Parameter(Mandatory=$true, HelpMessage="What kind of worker app: tiling or align")][ValidateSet("tiling","align")][System.String]$ApplicationType)

# Get nested stack name. ADD NEW STACKS HERE
if ($ApplicationType -eq "align"){
    $json = aws cloudformation describe-stack-resources --stack-name $StackName --logical-resource-id Alignment --output json 
    $WorkerStackName = ($json | ConvertFrom-Json).StackResources.PhysicalResourceId
}
if ($ApplicationType -eq "tiling"){
    $json = aws cloudformation describe-stack-resources --stack-name $StackName --logical-resource-id Tiling --output json 
    $WorkerStackName = ($json | ConvertFrom-Json).StackResources.PhysicalResourceId
}

# Where to put code deploy resources in S3. Fetches bucket and prefix from stack. 
$json = aws cloudformation describe-stacks --stack-name $StackName --query 'Stacks[0].Parameters[?ParameterKey==`BucketName`]' --output json 
$UploadBucket = ($json | ConvertFrom-Json).ParameterValue
$json = aws cloudformation describe-stacks --stack-name $StackName --query 'Stacks[0].Parameters[?ParameterKey==`S3Prefix`]' --output json 
$S3Prefix = ($json | ConvertFrom-Json).ParameterValue
$UploadKey = "$S3Prefix/stack_resources/$ApplicationType-cd.zip"
$UploadLocation = "s3://$UploadBucket/$UploadKey"


# logical ids for deployment infrastructure within stack (as defined in cloud formation templates)
# alignment.template and pipeline.template use the same logical IDs
$WorkerApplicationResource = "WorkerApplication"
$WorkerDeploymentGroupResource = "WorkerDeploymentGroup"


### Get names for our deployment resources 
$json = aws cloudformation describe-stack-resources --stack-name $WorkerStackName --logical-resource-id $WorkerApplicationResource --output json 
$WorkerApplicationName = ($json | ConvertFrom-Json).StackResources.PhysicalResourceId

$json = aws cloudformation describe-stack-resources --stack-name $WorkerStackName --logical-resource-id $WorkerDeploymentGroupResource --output json 
$WorkerDeploymentGroupName = ($json | ConvertFrom-Json).StackResources.PhysicalResourceId



#remove any prior release folder 
if (Test-Path release){
    Write-Host ".....Removing old release folder"
    Remove-Item release -Recurse -ErrorAction Ignore
}

#create folder structure 
mkdir release | Out-Null
cd release
mkdir Source | Out-Null

#copy source 
#assumes a built application in Landform/bin/Release 
Write-Host ".....Copying files to release folder"
Copy-Item -Recurse ..\..\..\Landform\bin\Release\* Source

#copy scripts. 
Copy-Item -Recurse ..\EC2Scripts\* .

#copy yaml. ADD NEW APPLICATION TYPES HERE
if ($ApplicationType -eq "tiling"){
    Copy-Item ..\mesh-appspec.yml appspec.yml
}
if ($ApplicationType -eq "align"){
    Copy-Item ..\align-appspec.yml appspec.yml
}


#push revision to s3
Write-Host ".....Pushing revision to s3 application $WorkerApplicationName"
aws deploy push --application-name $WorkerApplicationName --description "This is a revision for the Landform app" --ignore-hidden-files --s3-location $UploadLocation --source .

#deploy revision 
Write-Host ".....Deploying application to deployment group $WorkerDeploymentGroupName"
aws deploy create-deployment --application-name $WorkerApplicationName --s3-location bucket=$UploadBucket,key=$UploadKey,bundleType=zip --deployment-group-name $WorkerDeploymentGroupName

#return 
cd ..