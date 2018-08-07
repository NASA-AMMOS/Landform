LandformWeb is a node based server and browser based client for controlling the Landform cluster including but not limited to functionality of the Geometry Tiling Server.

Deployed at: https://landform.hi.jpl.nasa.gov

This repo consists of a backend [REST API](API.md) server and a frontend react app in `/client`.  In production the frontend app is pre-built with webpack and served as static files by the backend server.  During development a separate frontend server is used with hot module reloading.

### Dev Instructions
1. `npm install` in the root directory
1. `npm start` will start both the backend api server on port 8081 and the frontend react dev server on port 3000 (the frontend server will proxy backend routes to the backend server)
1. You can also run the api server and client servers independently with `npm run server` and `npm run client`.  This is convenient when doing backend dev so that you can independently reestart the backend server.
1. Typically you should not need to restart the frontend server for frontend dev because it uses hot module reloading.  However, if you modify the backend server you will need to restart it.  Note: CTRL-C may not work correctly to kill the backend server if you started it from a cygwin prompt; consider using a Git bash prompt or Windows `cmd` instead.
1. Go to http://localhost:3000 and follow the instructions to login and generate an API token.  Note that SSO login will only work when deployed to the production server URL given above, and LDAP login will only work when the server is within the JPL firewall (i.e. not deployed to AWS for production).
1. To re-deploy the production server: `deploy.bat aws_profile_name`

## Beanstalk Setup
1. http://goto.jpl.nasa.gov/awsconsole
1. Select region us-west-1 (North California)
1. Services -> Compute -> Elastic Beanstalk
1. Create application: "landform"
1. Create new Web sever eviornment
1. Page 1
   1. Environment Name and domain: landformweb
   1. Platform: docker
   1. Configure more options
1. Modify Software
   1. Envornmental properties
       * NODE_ENV = production
       * SESSION_SECRET - Something securley generated
       * TOKEN_SECRET - Something securley generated
       * SAML_CERT - Single sign on identity provider certificate, copy X509Certificate from [here](https://ssodev2.jpl.nasa.gov/oamfed/idp/metadata)
1. Modify instances 
    1. Instance type: t2.medium
1. Modify Capacity
    1. Load balanced
1. Modify load balancer
    1. ELB listener off
    1. Secure ELP listener port 443
    1. Select the SSL certificate ID (see last step in Setup HTTPS)
1. Modify security
    1. IAM instance profile: landlords
1. Modify network
    1. Visibility: Public (this may need to be false if creating inside a vpc)
    	. Load balancer subnets: us-west-1c	subnet-148d7971	172.31.16.0/20
    1. Instance settings Public IP Address: check
    	. Instance subnets: us-west-1c	subnet-148d7971	172.31.16.0/20
    1. Instance security groups: leave it alone, default creates a security group that can only talk to the load balancer which is what we want.
1. Create Environment!
1. Restrict to JPL only IPs
    1. Once the environment has loaded, look at the EC2 secuirty group in Instance settings. Make note of it
    1. Go to EC2 Dashboard and select security groups
    1. Search for the instance security group noted in step 1
    1. Look at the security group referenced in it's inbound source.  This is the ELB secuirty group
    1. Search for and select the ELB security group and change its inbound settings to:
        1. HTTPS TCP 443 128.149.0.0/16
        1. HTTPS TCP 443 137.78.0.0/16 
        1. HTTPS TCP 443 137.79.0.0/16
        1. HTTPS TCP 443 137.228.0.0/16
1. Adjust .elasticbeanstalk/config.yml in this repo as appropriate
1. Run deploy.bat with appropriate profile name
1. Setup HTTPS as seen below
1. Update Route53 for landform.hi.jpl.nasa.gov (on Jeffs acocunt) so that its CNAME record points at the landformweb*.elasticbeanstalk.com url
1. For the /api/pipeline/* endpoints to work, additional enviornmental variables must be setup.  Typically these are set when the beanstalk is created via cloud formation but can be manually set as needed.

### Setup HTTPS
1. Login to JPL Certificate manager at https://ssl.jpl.nasa.gov
1. Manage my certificates, select landform.hi.jpl.nasa.gov, save this info for later 
1. Go to AWS Certificate Manager
1. Import a certificate
1. Paste Certificate content into Certificate body
1. Paste Intermediate and Root certificate into certifcation chain (include the stuff ---- but not the labels between each cert)
1. Paste the contents of \\opslab-central\project\landform\webcert\landform.hi.jpl.nasa.gov.key in the private key field
1. Import
1. Edit name tag to be landform.hi.jpl.nasa.gov
1. Go to elastic beanstalk Load Balancing configuration
1. Select the SSL certificate ID.  These may appear as *.jpl.nasa.gov, in which case you need to use debug console to inspect the drop down and compare the ARN with what is listed in the AWS certificate manager.  Then count how many items down in the option list it is and select that one.  Not kidding.  This is how it works.

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

   
