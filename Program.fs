﻿module Program

open Pulumi

open Pulumi.FSharp
open Pulumi.FSharp.Aws.DynamoDB
open Pulumi.FSharp.Aws.DynamoDB.Inputs
open Pulumi.FSharp.Aws.S3
open Pulumi.FSharp.Aws.S3.Inputs
open Pulumi.FSharp.Aws.Iam
open Pulumi.FSharp.Aws.Iam.Inputs
open Pulumi.FSharp.Aws.Lambda
open Pulumi.FSharp.Aws.Lambda.Inputs

open Pulumi.Aws.S3
open Pulumi.Aws.DynamoDB
open Pulumi.Aws.Lambda


type StorageFunctionArgs = { bucket: Bucket; table: Table }

type StorageFunction(name: string, args: StorageFunctionArgs, opts: ComponentResourceOptions) as self =
    inherit ComponentResource("aws:StorageFunction", name, opts)

    let archive = (FileArchive("./function_code") :> Archive)

    let lambdaRole =
        ``role`` {
            name "lambdaExecutionRole"
            assumeRolePolicy
                """{
                            "Version": "2012-10-17",
                            "Statement": [
                                {
                                    "Action": "sts:AssumeRole",
                                    "Principal": {
                                        "Service": "lambda.amazonaws.com"
                                    },
                                    "Effect": "Allow",
                                    "Sid": ""
                                }
                            ]
                        }"""

            inlinePolicies
                [ ``roleInlinePolicy`` {
                      name "dynamodb-policy"
                      policy
                          """{
                            "Version": "2012-10-17",
                            "Statement": [
                                {
                                    "Action": "dynamodb:*",
                                    "Resource": "*",
                                    "Effect": "Allow",
                                    "Sid": ""
                                }
                            ]
                        }"""
                  }
                  ``roleInlinePolicy`` {
                      name "s3-policy"
                      policy
                          """{
                            "Version": "2012-10-17",
                            "Statement": [
                                {
                                    "Action": "s3:*",
                                    "Resource": "*",
                                    "Effect": "Allow",
                                    "Sid": ""
                                }
                            ]
                        }"""
                  } ]
        }

    do ``rolePolicyAttachment`` {
            name "lambdaBasicExecution"
            role lambdaRole.Id
            policyArn "arn:aws:iam::aws:policy/service-role/AWSLambdaBasicExecutionRole"
        } |> ignore

    let lambda =
        ``function`` {
            name "s3-file-watcher"
            runtime Runtime.Python3d10
            code archive
            handler "lambda_function.lambda_handler"
            role lambdaRole.Arn
            timeout 10
            functionTracingConfig { mode "Active" }
            functionEnvironment { variables [ ("TABLE_NAME", args.table.Name) ] }
        }


    do ``permission`` {
                name "s3-invoke-permission"
                action "lambda:InvokeFunction"
                ``function`` lambda.Name
                principal "s3.amazonaws.com"
                sourceArn args.bucket.Arn
        } |> ignore

    do ``bucketNotification`` {
            name "s3-object-put"
            bucket args.bucket.Id

            lambdaFunctions
                [ bucketNotificationLambdaFunction {
                      lambdaFunctionArn lambda.Arn
                      events [ "s3:ObjectCreated:*" ]
                  } ]
        } |> ignore

    do self.RegisterOutputs() |> ignore

    new(name: string, args: StorageFunctionArgs) = StorageFunction(name, args, ComponentResourceOptions())


let infra () =

    // Create an AWS resource (S3 Bucket)
    let bucket = ``bucket`` { name "file-storage" }

    let table =
        ``table`` {
            name "file-storage-db"
            readCapacity 1
            writeCapacity 1

            attributes
                [ tableAttribute {
                      name "filename"
                      resourceType "S"
                  }
                  tableAttribute {
                      name "timestamp"
                      resourceType "S"
                  } ]

            hashKey "filename"
            rangeKey "timestamp"
        }


    StorageFunction("file-storage-event-handler", { bucket = bucket; table = table })
        |> ignore

    // Export the name of the bucket
    dict [ ("bucketName", bucket.Id :> obj) ]


[<EntryPoint>]
let main _ = Deployment.run infra