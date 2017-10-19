#################################
## Gets the config file for a stack. 
## Allows devs to run landform.exe queuelisten on their machine and connect it to an AWS stack 
## Note: Keep in sync with UserData section of pipeline.template (in the autoscale group configuration) 
#################################

#name of stack 
param([Parameter(Mandatory=$true)][System.String]$StackName)

#logical ids for deployment infrastructure within stack (as defined in cloud formation template)
$JobQueueResource = "JobQueue"

### Get url for resource
$json = aws cloudformation describe-stack-resources --stack-name $StackName --logical-resource-id $JobQueueResource --output json 
$JobQueueUrl = ($json | ConvertFrom-Json).StackResources.PhysicalResourceId

### Save to json 

mkdir $HOME\\.landform -ea Ignore;
echo "`{`"JobQueue`":`"$JobQueueUrl`"`,`"PipelineName`":`"$StackName`"`}" > $HOME\\.landform\\pipelineworker.json