######################################################
## Deploy the stack in pipeline.template 
## If user specifies an existing stack name, CloudFormation generates and applies a change set 
##     Otherwise, a new stack is created. 
## 
######################################################

#name of stack 
param([Parameter(Mandatory=$true)][System.String]$StackName)

aws cloudformation deploy --template-file pipeline.template --stack-name $StackName

