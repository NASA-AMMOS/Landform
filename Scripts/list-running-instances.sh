#!/bin/sh

aws ec2 describe-instances --filters "Name=tag:Name,Values=*landform*" "Name=instance-state-name,Values=running" | grep -e InstanceId -e 'Value": "landform-' -e '"Name":'
