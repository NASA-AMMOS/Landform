#!/bin/sh

if [ $# -ne 1 ]; then
    echo "USAGE: create-queues.sh VENUE"
    exit 1
fi

venue=$1

aws --profile=credss-default sqs create-queue --queue-name m20-${venue}-ids-landform-tactical
aws --profile=credss-default sqs create-queue --queue-name m20-${venue}-ids-landform-tactical-fail
aws --profile=credss-default sqs create-queue --queue-name m20-${venue}-ids-landform-contextual
aws --profile=credss-default sqs create-queue --queue-name m20-${venue}-ids-landform-contextual-fail
aws --profile=credss-default sqs create-queue --queue-name m20-${venue}-ids-landform-contextual-worker
aws --profile=credss-default sqs create-queue --queue-name m20-${venue}-ids-landform-contextual-worker-fail
aws --profile=credss-default sqs create-queue --queue-name m20-${venue}-ids-landform-orbital-worker
aws --profile=credss-default sqs create-queue --queue-name m20-${venue}-ids-landform-orbital-worker-fail

echo "need to manually set access policies"

#TODO
#{
#  "Version": "2012-10-17",
#  "Id": "arn:aws-us-gov:sqs:us-gov-west-1:017717573760:m20-${venue}-ids-landform-contextual/SQSDefaultPolicy",
#  "Statement": [
#    {
#      "Sid": "will be generated",
#      "Effect": "Allow",
#      "Principal": "*",
#      "Action": "SQS:SendMessage",
#      "Resource": "arn:aws-us-gov:sqs:us-gov-west-1:017717573760:m20-${venue}-ids-landform-contextual",
#      "Condition": {
#        "StringEquals": {
#          "aws:SourceArn": "arn:aws-us-gov:sns:us-gov-west-1:017717573760:m20-${venue}-cs3-pipeline-topic"
#        }
#      }
#    }
#  ]
#}
