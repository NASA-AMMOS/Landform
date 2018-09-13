# LandformWeb AWS Setup

## Part 1: Elastic Beanstalk
1. http://goto.jpl.nasa.gov/awsconsole
1. Log in as landords/account_owner
1. Select region `us-west-1` (North California)
1. Services -> Compute -> Elastic Beanstalk
1. Create application: `landform`
1. Create new web sever environment
    1. Environment Name and domain: `landformweb-dev` (omit `-dev` for production)
    1. Platform: `docker`
    1. Configure more options
        1. Modify instances
            1. Instance type: `t2.medium`
            1. Instance security groups: leave it alone, default creates a security group that can only talk to the load balancer which is what we want.
    1. Modify Capacity -> Load balanced, min = 1, max = 1
    1. Modify security -> IAM instance profile: `landlords`
    1. Modify network
        1. Visibility: Public (this may need to be false if creating inside a vpc)
        1. Load balancer subnets: `us-west-1c subnet-148d7971 172.31.16.0/20`
        1. Instance subnets: same as load balancer
1. Create Environment!
    1. Wait for environment to be created, it takes a few minutes.

## Part 2: DNS
1. http://goto.jpl.nasa.gov/awsconsole
1. If you don't see Account: `jeffnorris (376XXXXXXXXX)` in your selectable roles, you need to ask Alex Menzies to add you to the LDAP group `aws.376XXXXXXXXX.power_user` (if you don't see a list of selectable roles, then you have only one role)
1. Sign in as that account
1. Select region `us-west-1` (North California)
1. Services -> Networking -> Route 53
1. Hosted zones -> `hi.jpl.nasa.gov`
1. Create record set
    1. Name: `landform-dev.hi.jpl.nasa.gov` (omit `-dev` for production)
    1. Type: `CNAME`
    1. TTL: `60`
    1. Value: `landformweb-dev.us-west-1.elasticbeanstalk.com` (omit `-dev` for production)
    1. Routing policy: `simple`
    1. Create

## Part 3: HTTPS
1. Generate certificate signing request.
    1. you will need a CLI that includes the openssl tool - on Windows use git bash
    1. if it exists, use `//opslab-central/condutor/project/landform/webcert/landform.hi.jpl.nasa.gov.key` (this is the same for testing and production)
        1. otherwise generate an RSA private key and save it there: `openssl genrsa > DEST` where DEST is the path above (careful, don't overwrite it if it already exists)
    1. generate CSR: `openssl req -new -key landform.hi.jpl.nasa.gov.key -out landform-dev.hi.jpl.nasa.gov.csr` (omit `-dev` for production, and again, don't overwrite any existing CSR)
        1. Country name: `US`
        1. State name: `California`
        1. Locality name: `Pasadena`
        1. Organization name: `NASA Jet Propulsion Laboratory`
        1. Organizational unit name: `OCIO`
        1. Common name: `landform-dev.hi.jpl.nasa.gov` (omit `-dev` for production)
        1. Email address: (empty)
        1. Challenge password: (empty)
        1. Company name: (empty)
1. Login to JPL Certificate manager at https://ssl.jpl.nasa.gov
    1. Manage my certificates -> if `landform-dev.hi.jpl.nasa.gov` already exists, use it (omit `-dev` for production)
    1. Otherwise, click Request a Certificate
        1. LDAP group: `landform` (this is the same for testing and production)
        1. Machine name: `landform-dev.hi.jpl.nasa.gov` (omit `-dev` for production)
        1. Server type: Apache
        1. CSR: paste entire contents of `landform-dev.hi.jpl.nasa.gov.csr` (omit `-dev` for production)
        1. wait a few minutes, you should get an email when the cert is ready
    1. Manage my certificates
        1. click on `landform-dev.hi.jpl.nasa.gov` (omit `-dev` for production)
        1. paste entire contents of certificate to `//opslab-central/conductor/project/landform/webcert/landform-dev.hi.jpl.nasa.gov.pem` (omit `-dev` for production).  It shold have a form like this
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
    1. Log in as landords/account_owner
    1. Select region `us-west-1` (North California)
    1. Security -> Certificate Manager
        1. If `landform-dev.hi.jpl.nasa.gov` (omit `-dev` for production) is not already in the list of certs -> Import a certificate
            1. Paste 
                ```
                -----BEGIN CERTIFICATE-----
                (cert)
                -----END CERTIFICATE-----
                ```
                from `landform-dev.hi.jpl.nasa.gov.pem` (omit `-dev` for production) into Certificate body
            1. Paste 
                ```
                -----BEGIN CERTIFICATE-----
                (intermediate cert)
                -----END CERTIFICATE-----
                -----BEGIN CERTIFICATE-----
                (root cert)
                -----END CERTIFICATE-----
                ```
                from landform.hi.jpl.nasa.gov.pem into Certificate chain
            1. Paste entire contents of `landform.hi.jpl.nasa.gov.key` into Certificate private key
            1. Review and Import -> Import
            1.  Open accordion for the new cert
            1. Edit name tag to be `landform-dev.hi.jpl.nasa.gov` (omit `-dev` for production)
        1.  Open accordion for the `landform-dev.hi.jpl.nasa.gov` cert (omit `-dev` for production)
            1. Note Details -> Identifier GUID 
    1. Services -> Compute -> Elastic Beanstalk
        1. Landform -> landformweb -> Configuration
        1. Modify load balancer
            1. Classic load balancer
            1. Turn off existing listener
            1. Add listener
                1. Listener port: `443`
                1. Listener protocol: `HTTPS`
                1. Instance port: `80`
                1. Instance protocol: `HTTP`
                1. Select the SSL certificate ID.  These may appear as `*.jpl.nasa.gov`, in which case you need to find the GUID matching Details -> Identifier for the cert in the AWS Certificate Manager.
            1. Apply

## Part 4: Restrict to JPL only IPs
1. http://goto.jpl.nasa.gov/awsconsole
1. Log in as landords/account_owner
1. Select region `us-west-1` (North California)
1. Services -> Compute -> Elastic Beanstalk
1. Landform -> landformweb -> Configuration
1. Instance settings -> note down EC2 security grup
1. Services -> Compute -> EC2 -> Security Groups
    1. Search for the instance security group noted in step 1
    1. Look at the security group referenced in it's inbound source.  This is the ELB security group.
    1. Search for and select the ELB security group and change its inbound settings to:
        1. `HTTPS TCP 443 128.149.0.0/16`
        1. `HTTPS TCP 443 137.78.0.0/16 `
        1. `HTTPS TCP 443 137.79.0.0/16`
        1. `HTTPS TCP 443 137.228.0.0/16`

## Part 5: Configure and Deploy App
1. http://goto.jpl.nasa.gov/awsconsole
    1. Log in as landords/account_owner
    1. Select region `us-west-1` (North California)
    1. Services -> Compute -> Elastic Beanstalk
    1. Landform -> landformweb -> Configuration
    1. Software -> Environment variables
        * `NODE_ENV`: `production` (we usually set `NODE_ENV=production` in deployments even for testing)
        * `SESSION_SECRET`: Something securley generated
        * `TOKEN_SECRET`: Something securley generated
        * `SAML_ENTRY_POINT`: `https://sso1.jpl.nasa.gov/oamfed/idp/initiatesso?providerid=https://landform.hi.jpl.nasa.gov` (use `ssoint` instead of `sso1` for testing)
        * `SAML_CERT`: Single sign on identity provider certificate, copy X509Certificate from https://sso1.jpl.nasa.gov/oamfed/idp/metadata (use `ssoint` instead of `sso1` for testing)
        * `TILE_SERVER_REGION`: `us-west-1`
        * `TILE_SERVER_VENUE_NAME`: `landformweb-dev` (omit `-dev` for production)
        * `TILE_SERVER_S3_URL`: `s3://landlords-dev` (we currently use this even for production)
    1. Apply
1. Follow instructions [above](#deployment) to deploy
