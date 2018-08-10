LandformWeb is a node based server and browser based client for controlling the Landform cluster including but not limited to functionality of the Geometry Tiling Server.

Deployed at https://landform.hi.jpl.nasa.gov.  This site is only accessible to JPL IP addresses.  For VPN access, use full tunnel mode.

This repo consists of a backend [REST API](API.md) server and a frontend react app in `/client`.  In production the frontend app is pre-built with webpack and served as static files by the backend server.  During development a separate frontend server is used with hot module reloading.  The react app configuration is managed with [create-react-app](https://github.com/facebook/create-react-app).

### Development
1. `npm install` in the root directory
1. `../Cloud/aws-login.py` or `winpty python ../Cloud/aws-login.py` if using git bash.  This will generate temporary AWS credentials in `$HOME/.aws/credentials`.
1. `npm start` will start both the backend api server on port 8081 and the frontend react dev server on port 3000 (the frontend server will proxy backend routes to the backend server)
1. You can also run the api server and client servers independently with `npm run server` and `npm run client`.  This is convenient when doing backend dev so that you can independently reestart the backend server.
1. Typically you should not need to restart the frontend server for frontend dev because it uses hot module reloading.  However, if you modify the backend server you will need to restart it.  Note: CTRL-C may not work correctly to kill the backend server if you started it from a cygwin prompt; consider using a Git bash prompt or Windows `cmd` instead.
1. Go to http://localhost:3000 and follow the instructions to login and generate an API token.  Note that SSO login will only work when deployed to the production server URL given above, and LDAP login will only work when the server is within the JPL firewall (i.e. not deployed to AWS for production).

### Deployment
1. Build landformweb.zip: `npm run build` - sugar for `npm install && npm run build-client && run npm bundle`
    1. `npm install` - installs node\_modules and client/node\_modules
    1. `npm run build-client` - runs `npm run build` to webpack client/build
    1. `npm run bundle` - creates landformweb.zip containing
        1. full archive of current git HEAD
        1. current client/build subtree
1. Follow the instructions in the header comments of `../Cloud/aws-login.py` to configure your machine with the AWS CLI Python tools, if you haven't already.
1. (optional) test the Docker container locally
    1. This will require a local installation of [Docker](https://www.docker.com) host.
    1. Run `../Cloud/aws-login.py` (`winpty python ../Cloud/aws-login.py` if using git bash) to generate temporary AWS credentials in \$HOME/.aws/credentials.
        1. if you have multiple roles, select arn:aws:iam::589270964471:role/account_owner
    1. `npm run local-deploy -- [-f|--force] [-i|--interactive] [-d|--debug]`. This will re-build the Docker container ("landformweb") and run it locally.  You can test it at http://localhost:8081.  Options:
        1. `--force`: use existing landformweb.zip even if it might be outdated
        1. `--interactive`: drop into a shell in the Docker container instead of running the server.  Note: if using git bash run `winpty node local-deploy.js -i ...` instead.
        1. `--debug`: set `LOG_LEVEL=silly` in the Docker container
1. Deploy to Elastic Beanstalk
    1. Run `../Cloud/aws-login.py` (`winpty python ../Cloud/aws-login.py` if using git bash) to generate temporary AWS credentials in \$HOME/.aws/credentials.
        1. if you have multiple roles, select arn:aws:iam::589270964471:role/account_owner
    1. `npm run deploy -- [environment-name] [-f|--force] [--profile=foo]`.  Options:
        1. `environment-name`: Upload to this Elastic Beanstalk environment instead of `landformweb`
        1. `--force`: use existing landformweb.zip even if it might be outdated
        1. `--profile=foo`: use AWS credentials profile `foo` instead of `default`
    1.  If you are working remotely by VPN you shouldn't be able to access the deployed site (https://landform.hi.jpl.nasa.gov) unless you use full tunnel, because we restrict access to the site to only JPL IP addresses.

## Setup

### Setup 1: Elastic Beanstalk
1. http://goto.jpl.nasa.gov/awsconsole
1. Log in as landords/account_owner
1. Select region us-west-1 (North California)
1. Services -> Compute -> Elastic Beanstalk
1. Create application: landform
1. Create new web sever environment
    1. Environment Name and domain: landformweb
    1. Platform: docker
    1. Configure more options
        1. Modify instances
            1. Instance type: t2.medium
            1. Instance security groups: leave it alone, default creates a security group that can only talk to the load balancer which is what we want.
    1. Modify Capacity -> Load balanced
    1. Modify security -> IAM instance profile: landlords
    1. Modify network
        1. Visibility: Public (this may need to be false if creating inside a vpc)
        1. Load balancer subnets: us-west-1c subnet-148d7971 172.31.16.0/20
        1. Instance subnets: same as load balancer
1. Create Environment!
    1. Wait for environment to be created, it takes a few minutes.

### Setup 2: DNS
1. http://goto.jpl.nasa.gov/awsconsole
1. If you don't see Account: jeffnorris (376XXXXXXXXX) in your selectable roles, you need to ask Alex Menzies to add you to the LDAP group aws.376XXXXXXXXX.power_user (if you don't see a list of selectable roles, then you have only one role)
1. Sign in as that account
1. Select region us-west-1 (North California)
1. Services -> Networking -> Route 53
1. Hosted zones -> hi.jpl.nasa.gov
1. Create record set
    1. Name: landform.hi.jpl.nasa.gov
    1. Type: CNAME
    1. TTL: 60
    1. Value: landformweb.us-west-1.elasticbeanstalk.com
    1. Routing policy: simple
    1. Create

### Setup 3: HTTPS
1. Generate certificate signing request.
    1. you will need a CLI that includes the openssl tool - on Windows use git bash
    1. if it exists, use //opslab-central/condutor/project/landform/webcert/landform.hi.jpl.nasa.gov.key
        1. otherwise generate an RSA private key and save it there: `openssl genrsa > DEST` where DEST is the path above (careful, don't overwrite it if it already exists)
    1. generate CSR: `openssl req -new -key landform.hi.jpl.nasa.gov.key -out landform.hi.jpl.nasa.gov.csr` (again, don't overwrite any existing CSR)
        1. Country name: US
        1. State name: California
        1. Locality name: Pasadena
        1. Organization name: NASA Jet Propulsion Laboratory
        1. Organizational unit name: OCIO
        1. Common name: landform.hi.jpl.nasa.gov
        1. Email address: (empty)
        1. Challenge password: (empty)
        1. Company name: (empty)
1. Login to JPL Certificate manager at https://ssl.jpl.nasa.gov
    1. Manage my certificates -> if landform.hi.jpl.nasa.gov already exists, use it
    1. Otherwise, click Request a Certificate
        1. LDAP group: landform
        1. Machine name: landform.hi.jpl.nasa.gov
        1. Server type: Apache
        1. CSR: paste entire contents of landform.hi.jpl.nasa.gov.csr
        1. wait a few minutes, you should get an email when the cert is ready
    1. Manage my certificates
        1. click on landform.hi.jpl.nasa.gov
        1. paste entire contents of certificate to //opslab-central/conductor/project/landform/webcert/landform.hi.jpl.nasa.gov.pem.  It shold have a form like this
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
    1. Select region us-west-1 (North California)
    1. Security -> Certificate Manager
        1. If landform.hi.jpl.nasa.gov is not already in the list of certs -> Import a certificate
            1. Paste 
                ```
                -----BEGIN CERTIFICATE-----
                (cert)
                -----END CERTIFICATE-----
                ```
                from landform.hi.jpl.nasa.gov.pem into Certificate body
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
            1. Paste entire contents of landform.hi.jpl.nasa.gov.key into Certificate private key
            1. Review and Import -> Import
            1.  Open accordion for the new cert
            1. Edit name tag to be landform.hi.jpl.nasa.gov
        1.  Open accordion for the landform.hi.jpl.nasa.gov cert
            1. Note Details -> Identifier GUID 
    1. Services -> Compute -> Elastic Beanstalk
        1. Landform -> landformweb -> Configuration
        1. Modify load balancer
            1. Classic load balancer
            1. Turn off existing listener
            1. Add listener
                1. Listener port: 443
                1. Listener protocol: HTTPS
                1. Instance port: 80
                1. Instance protocol: HTTP
                1. Select the SSL certificate ID.  These may appear as *.jpl.nasa.gov, in which case you need to find the GUID matching Details -> Identifier for the cert in the AWS Certificate Manager.
            1. Apply

### Setup 4: Restrict to JPL only IPs
1. http://goto.jpl.nasa.gov/awsconsole
1. Log in as landords/account_owner
1. Select region us-west-1 (North California)
1. Services -> Compute -> Elastic Beanstalk
1. Landform -> landformweb -> Configuration
1. Instance settings -> note down EC2 security grup
1. Services -> Compute -> EC2 -> Security Groups
    1. Search for the instance security group noted in step 1
    1. Look at the security group referenced in it's inbound source.  This is the ELB security group.
    1. Search for and select the ELB security group and change its inbound settings to:
        1. HTTPS TCP 443 128.149.0.0/16
        1. HTTPS TCP 443 137.78.0.0/16 
        1. HTTPS TCP 443 137.79.0.0/16
        1. HTTPS TCP 443 137.228.0.0/16

### Setup 5: Configure and Deploy App
1. http://goto.jpl.nasa.gov/awsconsole
    1. Log in as landords/account_owner
    1. Select region us-west-1 (North California)
    1. Services -> Compute -> Elastic Beanstalk
    1. Landform -> landformweb -> Configuration
    1. Software -> Environment variables
        * NODE\_ENV: production
        * SESSION\_SECRET - Something securley generated
        * TOKEN\_SECRET - Something securley generated
        * SAML\_CERT - Single sign on identity provider certificate, copy X509Certificate from [here](https://ssodev2.jpl.nasa.gov/oamfed/idp/metadata)
        * TILE\_SERVER\_REGION: us-west-1
        * TILE\_SERVER\_VENUE\_NAME: webdevtiles (TODO)
        * TILE\_SERVER\_S3\_URL: s3://landlords-dev
    1. Apply
1. Follow instructions [above](#deployment) to deploy

# Test Procedures

## Authentication Test Procedures

### SSO Test Procedure
1. Note this will only work on https://landform.hi.jpl.nasa.gov because that is the domain SSO is configured to use.
2. Click `API Token` and confirm the response is `not authenticated`.
3. Click `Login (SSO)`.
4. Enter JPL credentials.
5. Click `API Token` and confirm the response is a web token - copy the token ID for later.
6. Click `Logout`.
7. Click `API Token` and confirm the response is `not authenticated`.

### LDAP Test Procedure
1. Note that this will not work on https://landform.hi.jpl.nasa.gov because it is running on a cloud instance and does not currently have access to JPLs internal network / LDAP server. However, it will work when the server is run locally or on a JPL host machine.
2. If your JPL user is not in the landform LDAP group contact Alex Menzies to be added
3. Click `Login (LDAP)` 
4. Enter JPL credentials
5. Click `API Token` and confirm the response is a web token - copy the token ID for later
6. Click `Logout`
7. Click `API Token` and confirm the response is `not authenticated`

## REST API Test Procedure
**TODO this needs to be updated for the [new API](API.md)**

This procedure is designed to be excuted from the Windows 10 command prompt (`cmd`).  It may not run correctly on other command prompts such as cygwin, Git bash, etc.  It runs tests against the server currently deployed on https://landform.hi.jpl.nasa.gov.  To instead test against a server run locally or on a JPL host machine, use LDAP login instead of SSO and substitute the URL to the machine in the commands below.

1. Login and get an API token using either the SSO test procedure above.  Copy the token to the system clipboard, and then paste it on the windows command line to set a temporary environment variable by running a command like this:

   `set API_TOKEN=PASTED_TOKEN`

   Perform the remaining steps of this procedure in the same command window.

2. Create a project:

   `curl -d "{\"name\":\"test-project-name\"}" -H "Content-Type: application/json" -H "x-landform-token: %API_TOKEN%" -X POST https://landform.hi.jpl.nasa.gov/api/project`

3. List projects

   `curl -H "x-landform-token: %API_TOKEN%" -X GET https://landform.hi.jpl.nasa.gov/api/project`

4. Get data about a project

   `curl -H "x-landform-token: %API_TOKEN%" -X GET https://landform.hi.jpl.nasa.gov/api/project/test-project-name`

5. Delete a project

   `curl -H "x-landform-token: %API_TOKEN%" -X DELETE https://landform.hi.jpl.nasa.gov/api/project/test-project-name`

   
