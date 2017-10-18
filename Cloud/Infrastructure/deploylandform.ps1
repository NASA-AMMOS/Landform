###############################
## Deploys current release build of landform to specified Application/Deployment Group
## (does not build project) 
###############################

#name of stack 
param([Parameter(Mandatory=$true)][System.String]$StackName)

$UploadKey = "gailin/landform-cd.zip"
$UploadBucket = "landlords-dev"
$UploadLocation = "s3://$UploadBucket/$UploadKey"


#logical id for deployment infrastructure within stack 
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
#NOTE: CodeDeploy only executes powershell scripts in the root directory of the release (the directory containing appspec)
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