###############################
## Deploys current release build of landform to Application/Deployment Group for user-specified stack
## Assumes a dev stack. S3 upload paths should change for prod
## (does not build project) 
## Note: If you have RPCed into a worker and started landform yourself, this will *fail*. 
##       Either terminate that instance or manually kill the Landform you started
###############################

#name of stack 
param([Parameter(Mandatory=$true)][System.String]$StackName)

#Where to put code deploy resources in S3. Change as needed
$UploadKey = "pipeline_resources/landform-cd-$StackName.zip"
$UploadBucket = "landlords-dev"
$UploadLocation = "s3://$UploadBucket/$UploadKey"


#logical ids for deployment infrastructure within stack (as defined in cloud formation template)
$WorkerApplicationResource = "WorkerApplication"
$WorkerDeploymentGroupResource = "WorkerDeploymentGroup"


### Get names for each of our resources 
$json = aws cloudformation describe-stack-resources --stack-name $StackName --logical-resource-id $WorkerApplicationResource --output json 
$WorkerApplicationName = ($json | ConvertFrom-Json).StackResources.PhysicalResourceId

$json = aws cloudformation describe-stack-resources --stack-name $StackName --logical-resource-id $WorkerDeploymentGroupResource --output json 
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

#copy yaml 
Copy-Item ..\appspec.yml appspec.yml

#push revision to s3
Write-Host ".....Pushing revision to s3 application $WorkerApplicationName"
aws deploy push --application-name $WorkerApplicationName --description "This is a revision for the Landform app" --ignore-hidden-files --s3-location $UploadLocation --source .

#deploy revision 
Write-Host ".....Deploying application to deployment group $WorkerDeploymentGroupName"
aws deploy create-deployment --application-name $WorkerApplicationName --s3-location bucket=$UploadBucket,key=$UploadKey,bundleType=zip --deployment-group-name $WorkerDeploymentGroupName

#return 
cd ..