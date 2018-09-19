# LandformWeb AWS Setup
These instructions detail how to set up and deploy Landform to AWS.  The AWS services used by Landform are
* Elastic Compute Cloud ([EC2](https://aws.amazon.com/ec2))
* Simple Cloud Storage Service ([S3](https://aws.amazon.com/free/storage))
* [DynamoDB](https://aws.amazon.com/dynamodb)
* Simple Queue Service ([SQS](https://aws.amazon.com/sqs))
* Elastic Beanstalk ([EB](https://aws.amazon.com/elasticbeanstalk))
* Identity & Access Management ([IAM](https://aws.amazon.com/iam))
* Managed Cloud DNS ([Route 53](https://aws.amazon.com/route53)) (optional)
* [Certificate Manager](https://aws.amazon.com/certificate-manager)

A full deployment of Landform includes
1. a master server running on an EC2 instance managed with Elastic Beanstalk
2. a group of workers running on one or more EC2 instances managed as an EC2 Auto Scaling Group.

The master server includes
* a web interface that can be securely accessed by end users
* a REST API that can be used to securely integrate Landform with other systems
* a backend task which orchestrates Landform workflows.

The workers perform tasks defined by the master, and are not externally accessible.

Each deployment of Landform on AWS is configured with a unique venue name used to partition the DynamoDB, S3, and SQS entries specific to that venue.

## Use Cases
This document is written for three use cases:
1. administrators of end-use deployments of Landform within JPL
1. Landform developers performing internal development or deployment
1. Landform administrators maintaining a Landform production deployment.

If you are administering an end-use deployment outside the Landform team then you can, or in some cases must, substitute your own info for the following fields below:
* AWS account
* Elastic Beanstalk application name
* Elastic Beanstalk environment name
* DNS provider, DNS name, and HTTPS certificate information
* SAML entry point
* venue name

For the Landform team the typical values are noted below.  For development and integration testing use the `-dev` names, e.g. `landformweb-dev`, for production omit `-dev`.

## 1: Create Elastic Beanstalk Environment for Master Server
This step creates the Elastic Beanstalk application and environment into which the Landform master server will be deployed.

1. http://goto.jpl.nasa.gov/awsconsole
1. log in as `landords/account_owner` (internal Landform use only, otherwise use your own AWS account)
1. Select region `us-west-1` (North California)
1. Services -> Compute -> Elastic Beanstalk
1. Create application: `Landform` (recommended, but can be any name)
1. Create new web sever environment
    1. Environment Name: `landformweb[-dev]` (recommended, but can be any name)
    1. Platform: `docker`
    1. Configure more options
        1. Modify instances
            1. Instance type: `t2.medium`
            1. Instance security groups: default creates a security group that can only talk to the load balancer which is what we want
    1. Modify Capacity -> Load balanced, min = 1, max = 1
    1. Modify security -> IAM instance profile: `landlords` (internal Landform use only, otherwise use your own IAM profile)
    1. Modify network
        1. Visibility: Public
        1. Load balancer subnets: `us-west-1c subnet-148d7971 172.31.16.0/20`
        1. Instance subnets: same as load balancer
1. Create Environment - it takes a few minutes

## 2: Configure DNS
This step is optional.  It configures a public DNS entry to redirect to the Elastic Beanstalk environment configured above.  The Landform team uses the Amazon Route 53 DNS service, but any DNS provider that supports CNAME records should work.

The landform team uses this to redirect `https://landform[-dev].hi.jpl.nasa.gov` to `https://landformweb[-dev].us-west-1.elasticbeanstalk.com`.

If you forgo this step you can still access the Landform master server at a URL like the latter.

1. http://goto.jpl.nasa.gov/awsconsole
1. log in as `jeffnorris/power_user` (internal Landform use only, otherwise use your own AWS account)
1. Select region `us-west-1` (North California)
1. Services -> Networking -> Route 53
1. Hosted zones -> `hi.jpl.nasa.gov` (internal Landform use only, otherwise use your own zone)
1. Create record set
    1. Name: `landform[-dev].hi.jpl.nasa.gov` (internal Landform use only, otherwise use your own DNS name)
    1. Type: `CNAME`
    1. TTL: `60`
    1. Value: `landformweb[-dev].us-west-1.elasticbeanstalk.com` - this must match the Elastic Beanstalk environment created above
    1. Routing policy: `simple`
    1. Create

## 3: Configure HTTPS
This step uses the JPL and AWS certificate managers to generate and deploy cryptographic certificates to enable secure HTTPS connection to the Landform master server.

The overall flow is
1. generate an RSA private key
1. use the key to sign a certificate signing request (CSR)
1. submit the CSR to the JPL certificate manager to generate an HTTPS certificate (PEM)
1. register the certificate and key to the AWS certificate manager 
1. associate the certificate with an Elastic Beanstalk load balancer listener which will proxy HTTPS communication with the Landform master server.

For internal Landform use we keep an archive of the generated key, CSR, and PEM files, so be sure to discuss with the team before creating new ones.

Most steps below require the end-user DNS name for your Landform master server, e.g. `landform[-dev].hi.jpl.nasa.gov`.

1. Generate certificate signing request.  You will need a CLI that includes the `openssl` tool - on Windows one option is [Git bash](https://gitforwindows.org).
    1. generate an RSA private key: `openssl genrsa > landform.hi.jpl.nasa.gov.key` (recommended for internal Landform use, otherwise any filename)
    1. generate CSR: `openssl req -new -key KEYFILE -out DNS_NAME.csr` where `KEYFILE` is the RSA private key you generated above, and `DNS_NAME` is your Landform master server DNS name.
        * Country name: `US`
        * State name: `California`
        * Locality name: `Pasadena`
        * Organization name: `NASA Jet Propulsion Laboratory`
        * Organizational unit name: `OCIO`
        * Common name: your Landform master server DNS name
        * Email address: (empty)
        * Challenge password: (empty)
        * Company name: (empty)
1. Login to JPL Certificate manager at https://ssl.jpl.nasa.gov
1. Manage my certificates -> Request a Certificate
    1. LDAP group: `landform` (internal Landform use only, otherwise use your own LDAP group)
    1. Machine name: your Landform master server DNS name
    1. Server type: Apache
    1. CSR: paste entire contents of the CSR here
    1. wait a few minutes, you should get an email when the cert is ready
1. Manage my certificates
    1. click on your Landform master server DNS name
    1. paste entire contents of certificate to `landform[-dev].hi.jpl.nasa.gov.pem` (recommended for internal Landform use, otherwise any filename).  It shold have a form like this
        ```
        CERTIFICATE:
        -----BEGIN CERTIFICATE-----
        (cert)
        -----END CERTIFICATE-----
        INTERMEDIATE CERTIFICATE:
        -----BEGIN CERTIFICATE-----
        (intermediate cert)
        -----END CERTIFICATE-----
        ROOT CERTIFICATE:
        -----BEGIN CERTIFICATE-----
        (root cert)
        -----END CERTIFICATE-----
        ```
1. http://goto.jpl.nasa.gov/awsconsole
1. Log in as `landords/account_owner` (internal Landform use only, otherwise use your own AWS account)
1. Select region `us-west-1` (North California)
    1. Security -> Certificate Manager -> Import a certificate
        1. Paste 
            ```
            -----BEGIN CERTIFICATE-----
            (cert)
            -----END CERTIFICATE-----
            ```
            from your PEM file into the certificate body field.
        1. Paste 
            ```
            -----BEGIN CERTIFICATE-----
            (intermediate cert)
            -----END CERTIFICATE-----
            -----BEGIN CERTIFICATE-----
            (root cert)
            -----END CERTIFICATE-----
            ```
            from your PEM file into the certificate chain field.
        1. Paste entire contents of your RSA private key file into the certificate private key field.
        1. Review and Import -> Import
        1. Open accordion for the cert
           1. Edit name tag to be your Landform master server DNS name
        1. Open accordion for the cert
           1. Details -> Identifier GUID 
           1. Make a note of the GUID
    1. Services -> Compute -> Elastic Beanstalk
        1. Navigate into the Elastic Beanstalk application and environment you configured above
        1. Configuration -> Modify load balancer
            1. Classic load balancer
            1. Turn off existing listener
            1. Add listener
                * Listener port: `443`
                * Listener protocol: `HTTPS`
                * Instance port: `80`
                * Instance protocol: `HTTP`
                * Select the SSL certificate ID.  These may appear as `*.jpl.nasa.gov`, in which case you need to find the GUID matching the cert in the AWS Certificate Manager.
            1. Apply

## 4: Restrict to JPL IPs
This step is optional but recommended.  It restricts access to your Landform master server to clients within the JPL IP address space.

1. http://goto.jpl.nasa.gov/awsconsole
1. Log in as `landords/account_owner` (internal Landform use only, otherwise use your own AWS account)
1. Select region `us-west-1` (North California)
1. Services -> Compute -> Elastic Beanstalk
1. Navigate into the Elastic Beanstalk application and environment you configured above
1. Configuration -> Instance settings
   1. Note down the EC2 security group
1. Services -> Compute -> EC2 -> Security Groups
    1. Search for the instance security group noted above
    1. Look at the security group referenced in its inbound source.  This is the ELB security group.
    1. Search for and select the ELB security group and change its inbound settings to:
        * `HTTPS TCP 443 128.149.0.0/16`
        * `HTTPS TCP 443 137.78.0.0/16`
        * `HTTPS TCP 443 137.79.0.0/16`
        * `HTTPS TCP 443 137.228.0.0/16`

## 5: Configure Elastic Beanstalk Environment for Landform Master Server
This step configures the Elastic Beanstalk environment with specifics of your deployment for the Landform master server.

1. http://goto.jpl.nasa.gov/awsconsole
1. Log in as `landords/account_owner` (internal Landform use only, otherwise use your own AWS account)
1. Select region `us-west-1` (North California)
1. Services -> Compute -> Elastic Beanstalk
1. Navigate into the Elastic Beanstalk application and environment you configured above
1. Software -> Environment variables
    * `NODE_ENV`: `production`
    * `SESSION_SECRET`: any private string
    * `TOKEN_SECRET`: any private string
    * `SAML_ENTRY_POINT`: `https://sso1.jpl.nasa.gov/oamfed/idp/initiatesso?providerid=https://landform.hi.jpl.nasa.gov`(for internal Landform use only, otherwise contact JPL IT to set up SSO)
      * use `ssoint` instead of `sso1` for development and integration testing 
    * `SAML_CERT`: Single sign on identity provider certificate, copy X509Certificate from https://sso1.jpl.nasa.gov/oamfed/idp/metadata
      * use `ssoint` instead of `sso1` for development and integration testing
    * `TILE_SERVER_REGION`: `us-west-1`
    * `TILE_SERVER_VENUE_NAME`: `landformweb[-dev]` (recommended, but can be any name) - this is the Landform venue name and it must match the venue name in the Landform worker configuration
    * `TILE_SERVER_S3_URL`: `s3://landlords-dev` (internal landform use only, otherwise use your own S3 bucket)
    * `TILE_SERVER_LDAP_GROUP`: `landform` (internal landform use only, otherwise use your own LDAP group)
1. Apply

## 6: Deploy Landform Master Server Release
1. The following instructions assume you have a `landformweb-VERSION.zip` bundle.  For command-line deployment in the context of Landform development you can also follow the alternate instructions in the [README](../README.md).
1. http://goto.jpl.nasa.gov/awsconsole
1. Log in as landords/account_owner
1. Select region `us-west-1` (North California)
1. Services -> Compute -> Elastic Beanstalk
1. Navigate into the Elastic Beanstalk application and environment you configured above
1. Upload and Deploy
1. Choose File: `landformweb-VERSION.zip`
1. Version Label: VERSION
1. Deploy - it takes a few minutes

## 7: Configure EC2 Auto Scale Group for Landform Workers
TODO

## 8: Deploy Landform Worker Release
TODO
