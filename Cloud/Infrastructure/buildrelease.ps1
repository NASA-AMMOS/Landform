$AppName = "deploy-to-me-WorkerApplication-1ANN49X1LYE2N"
$DeployGroup = "deploy-to-me-WorkerDeploymentGroup-16L008Y7TNT3D"
$UploadKey = "gailin/landform-cd.zip"
$UploadBucket = "landlords-dev"
$UploadLocation = "s3://$UploadBucket/$UploadKey"

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
Copy-Item -Recurse ..\Scripts\* .

#copy yaml 
Copy-Item ..\appspec.yml appspec.yml

#push revision to s3
Write-Host ".....Pushing revision to s3 application $AppName"
aws deploy push --application-name $AppName --description "This is a revision for the Landform app" --ignore-hidden-files --s3-location $UploadLocation --source .

#deploy revision 
Write-Host ".....Deploying application to deployment group $DeployGroup"
aws deploy create-deployment --application-name deploy-to-me-WorkerApplication-1ANN49X1LYE2N --s3-location bucket=$UploadBucket,key=$UploadKey,bundleType=zip --deployment-group-name $DeployGroup

#return 
cd ..