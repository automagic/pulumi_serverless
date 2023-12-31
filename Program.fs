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
open Pulumi.Aws.Lambda
open Pulumi.Aws.Iam
open Pulumi.Aws.Iam.Inputs


type StorageWatcherArgs = { bucket: Bucket }

type StorageWatcher(name: string, args: StorageWatcherArgs, opts: ComponentResourceOptions) as self =
    inherit ComponentResource("aws:StorageWatcher", name, opts)

    let tableName = name + "-db"
    let table =
        ``table`` {
            name tableName
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

    let archive = (FileArchive("./function_code") :> Archive)

    let arPolicy =
        GetPolicyDocument.Invoke(
            GetPolicyDocumentInvokeArgs(
                Statements =
                    inputList
                        [ GetPolicyDocumentStatementInputArgs(
                              Actions = inputList [ "sts:AssumeRole" ],
                              Principals =
                                  inputList
                                      [ GetPolicyDocumentStatementPrincipalInputArgs(
                                            Type = input "Service",
                                            Identifiers = inputList [ "lambda.amazonaws.com" ]
                                        ) ]
                          ) ]
            )
        )

    let s3Policy =
        GetPolicyDocument.Invoke(
            GetPolicyDocumentInvokeArgs(
                Statements =
                    inputList
                        [ GetPolicyDocumentStatementInputArgs(
                              Actions = inputList [ "s3:*" ],
                              Resources = inputList [ io args.bucket.Arn ]
                          )]
            )
        )

    let dynamoPolicy =
        GetPolicyDocument.Invoke(
            GetPolicyDocumentInvokeArgs(
                Statements =
                    inputList
                        [ GetPolicyDocumentStatementInputArgs(
                              Actions = inputList [ "dynamodb:*" ],
                              Resources = inputList [ io table.Arn ]
                          ) ]
            )
        )

    let lambdaRole =
        ``role`` {
            name "lambdaExecutionRole"
            assumeRolePolicy (arPolicy.Apply(fun p -> p.Json))

            inlinePolicies
                [
                  ``roleInlinePolicy`` {
                      name "dynamodb-policy"
                      policy (dynamoPolicy.Apply(fun p -> p.Json))
                  }
                  ``roleInlinePolicy`` {
                      name "s3-policy"
                      policy (s3Policy.Apply(fun p -> p.Json))
                  } ]
        }

    do
        ``rolePolicyAttachment`` {
            name "lambdaBasicExecution"
            role lambdaRole.Id
            policyArn "arn:aws:iam::aws:policy/service-role/AWSLambdaBasicExecutionRole"
        }
        |> ignore

    let functionName = $"{name}-func"
    let lambda =
        ``function`` {
            name functionName
            runtime Runtime.Python3d10
            code archive
            handler "lambda_function.lambda_handler"
            role lambdaRole.Arn
            timeout 10
            functionTracingConfig { mode "Active" }
            functionEnvironment { variables [ ("TABLE_NAME", table.Name) ] }
        }


    do
        ``permission`` {
            name "s3-invoke-permission"
            action "lambda:InvokeFunction"
            ``function`` lambda.Name
            principal "s3.amazonaws.com"
            sourceArn args.bucket.Arn
        }
        |> ignore

    do
        ``bucketNotification`` {
            name "s3-object-put"
            bucket args.bucket.Id

            lambdaFunctions
                [ bucketNotificationLambdaFunction {
                      lambdaFunctionArn lambda.Arn
                      events [ "s3:ObjectCreated:*" ]
                  } ]
        }
        |> ignore

    do self.RegisterOutputs() |> ignore

    new(name: string, args: StorageWatcherArgs) = StorageWatcher(name, args, ComponentResourceOptions())


let infra () =

    let bn = "file-storage"

    // Create an AWS resource (S3 Bucket)
    let bucket = ``bucket`` { name bn }

    StorageWatcher($"{bn}-watcher", { bucket = bucket; })
    |> ignore

    // Export the name of the bucket
    dict [ ("bucketName", bucket.Id :> obj) ]


[<EntryPoint>]
let main _ = Deployment.run infra