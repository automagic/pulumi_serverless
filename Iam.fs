type Principal = {
    Service: string;
}

type PolicyStatement = {
    Action: string;
    Effect: string;
    Sid: string;
    Resource: seq<string> option;
    Principal: Principal option;
}

type PolicyDocument = {
    Version: string;
    Statement: seq<PolicyStatement>
}