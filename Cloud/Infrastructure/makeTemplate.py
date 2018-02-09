#!/usr/bin/env python3
# 2/9/2018
# Charles O. Goddard

from troposphere import Ref, Template, Join, Parameter
import troposphere.ec2 as ec2
from troposphere.applicationautoscaling import (
    ScalableTarget,
    StepAdjustment,
    TargetTrackingScalingPolicyConfiguration,
    PredefinedMetricSpecification,
    ScalingPolicy,
)
from troposphere.dynamodb import (KeySchema, AttributeDefinition,
                                  ProvisionedThroughput)
from troposphere.dynamodb import Table


class DynamoTable(object):
    def __init__(self, name, namePrefix, fields, readCapacity, writeCapacity, hashKey=None, rangeKey=None):
        self.name = name
        self.namePrefix = namePrefix
        self.fields = fields
        self.hashKey = hashKey
        self.rangeKey = rangeKey
        self.readCapacity = readCapacity
        self.writeCapacity = writeCapacity

    def define(self, template, autoScale=True, autoScaleArn=None):
        attribs = []
        keySchema = []

        for key in self.fields:
            attribs.append(AttributeDefinition(
                AttributeName=key,
                AttributeType=self.fields[key]
                ))
        if self.hashKey:
            keySchema.append(KeySchema(AttributeName=self.hashKey, KeyType="HASH"))
        if self.rangeKey:
            keySchema.append(KeySchema(AttributeName=self.rangeKey, KeyType="RANGE"))

        print(repr(self.name))
        res = template.add_resource(Table(
            self.name,
            TableName=Join("", [self.namePrefix, self.name]),
            AttributeDefinitions = attribs,
            KeySchema = keySchema,
            ProvisionedThroughput = ProvisionedThroughput(
                ReadCapacityUnits=self.readCapacity,
                WriteCapacityUnits=self.writeCapacity
                )
            ))
        if autoScale:
            def doOp(op):
                target = ScalableTarget(
                    self.name + op + "Target",
                    MaxCapacity=50,
                    MinCapacity=5,
                    ResourceId=Join("/", ["table", Ref(res)]),
                    RoleARN=autoScaleArn,
                    ScalableDimension="dynamodb:table:"+op+"CapacityUnits",
                    ServiceNamespace="dynamodb"
                    )
                policy = ScalingPolicy(
                    self.name + op + "Policy",
                    PolicyName="WriteAutoScalingPolicy",
                    PolicyType="TargetTrackingScaling",
                    ScalingTargetId=Ref(target),
                    TargetTrackingScalingPolicyConfiguration=TargetTrackingScalingPolicyConfiguration(
                        TargetValue=50,
                        ScaleInCooldown=60,
                        ScaleOutCooldown=60,
                        PredefinedMetricSpecification=PredefinedMetricSpecification(PredefinedMetricType="DynamoDB"+op+"CapacityUtilization")
                        )
                    )
                template.add_resource(target)
                template.add_resource(policy)
            doOp("Read")
            doOp("Write")
        return res

def alignment_pipeline():
    res = Template()

    scalingArn = res.add_parameter(Parameter(
        "TableAutoscalingRoleArn",
        Type="String",
        Default="arn:aws:iam::589270964471:role/AutoscalingServiceRole",
        Description="Role assumed by autoscaling agents for tables."
        ))

    tablePrefix = res.add_parameter(Parameter(
        "TablePrefix",
        Type="String",
        Description="The table name prefix. Workers will use this to publish to their tables even if multiple stacks run in the same region."
        ))

    def tableName(suffix):
        return Join("", [Ref(tablePrefix), suffix])

    projects = DynamoTable("Projects", Ref(tablePrefix), {
        "Name":"S",
        "InputPath":"S",
        "ProductPath":"S"
        }, 5, 5, hashKey='Name').define(res, autoScaleArn=Ref(scalingArn))
    frames = DynamoTable("Frames", Ref(tablePrefix), {
            "Name":"S",
            "ProjectName":"S",
            "ParentName":"S"
        }, 5, 5, hashKey='Name', rangeKey='ProjectName').define(res, autoScaleArn=Ref(scalingArn))
    # frames = DynamoTable("Frames", Ref(tablePrefix), {
    #         "Name":"S",
    #         "ProjectName":"S",
    #         "ParentName":"S"
    #     }, 5, 5, hashKey='Name', rangeKey='ProjectName').define(res, autoScaleArn=Ref(scalingArn))

    return res

print(alignment_pipeline().to_json())