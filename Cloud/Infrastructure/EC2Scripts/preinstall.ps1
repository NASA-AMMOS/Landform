#############################################
# am I running in 32 bit shell?
#############################################
if ($pshome -like "*syswow64*") {
 
  Write-Warning "Restarting script under 64 bit powershell"
 
  # relaunch this script under 64 bit shell
  & (join-path ($pshome -replace "syswow64", "sysnative") powershell.exe) -file `
    (join-path $psscriptroot $myinvocation.mycommand) @args
 
  # exit 32 bit script
  exit
}

#aw geez guys
#[Environment]::SetEnvironmentVariable("JOB_QUEUE", "https://sqs.us-west-1.amazonaws.com/589270964471/deploy-to-me-JobQueue-11ZCQBIFKHEJ0", "Machine")


## Additional environment variables PIPELINE_NAME and JOB_QUEUE are set 
## by the template on resource creation. You can overwrite those defaults here. 
## You can also use the AWS CLI to connect to other amazon services here if neccecary 

#Actually no you can't because CodeDeploy can't access env vars >.<