#!/bin/sh

aws ec2 describe-instances --filters "Name=tag:Name,Values=*landform*" | grep -e InstanceId -e 'Value": "landform-' -e '"Name":'
