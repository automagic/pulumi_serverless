import logging
import boto3
import os
import urllib.parse

from datetime import datetime

logger = logging.getLogger()
logger.setLevel(logging.INFO)


def write_table(filename, timestamp):

    dynamo = boto3.resource('dynamodb')

    table = dynamo.Table(os.getenv('TABLE_NAME'))

    table.put_item(
        Item={
                'filename':  filename,
                'timestamp': timestamp
            }
    )


def lambda_handler(event, context):

    bucket_name = event['Records'][0]['s3']['bucket']['name']
    object_key = urllib.parse.unquote_plus(event['Records'][0]['s3']['object']['key'], encoding='utf-8')

    now = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    file = f"s3://{bucket_name}/{object_key}"

    write_table(file, now)

    logger.info(f"{file} uploaded at {now}")