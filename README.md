# AI Service Configuration

This application now uses Amazon Bedrock Claude API for AI-powered method summaries.

## AWS Configuration

### Option 1: AWS Credentials File
Create a credentials file at `~/.aws/credentials`:
```ini
[default]
aws_access_key_id = YOUR_ACCESS_KEY_ID
aws_secret_access_key = YOUR_SECRET_ACCESS_KEY
```

### Option 2: Environment Variables
Set the following environment variables:
- `AWS_ACCESS_KEY_ID`
- `AWS_SECRET_ACCESS_KEY`
- `AWS_REGION` (optional, defaults to us-east-1)

### Option 3: IAM Roles (Recommended for EC2/ECS)
If running on AWS infrastructure, use IAM roles instead of hardcoded credentials.

## AWS Permissions Required

Your AWS credentials need the following permissions:
```json
{
    "Version": "2012-10-17",
    "Statement": [
        {
            "Effect": "Allow",
            "Action": [
                "bedrock:InvokeModel"
            ],
            "Resource": "arn:aws:bedrock:*::foundation-model/anthropic.claude-3-5-sonnet-20241022-v2:0"
        }
    ]
}
```