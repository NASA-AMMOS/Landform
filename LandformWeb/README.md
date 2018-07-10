# Landform web console
LandformWeb is a node based server and browser based client for controlling the Landform cluster including but not limited to functionality of the Geometry Tiling Server.

### Current Deployment
Landlord pub account in the N California region
https://landform.hi.jpl.nasa.gov/

# This repo consists of two parts
1. An api server implemented in `/server.js`
1. A client side react app implemented in `/client`

# Run in development locally
1. `npm install` in the root directory
1. `cd client` and run `npm install`
1. `cd ..` back to the root
1. `npm start` will start both the api server on port 8081 and the react dev server on port 3000 (which will proxy requests to the api server)
1. go to http://localhost:3000/
1. note that http://localhost:8081/ will take you to the api server and will render static content from the build directory.  However you will need to run `cd client` and run `npm run build` to generate the static assets.
1. You can also run the api server and client servers independently with `npm run server` and `npm run client`

# Debug workflow
1. Use postman to login via POST to /ldap/login.  This will supply an API token cookie for subsequent requests
1. You can also go to /apiToken to generate a token that can be included under the header key 'x-landform-token'

# To Deploy
1. If this is a fresh checkout, run `npm install` in both the `root` and `client` directories.  Then cd back up to `root`
1. `deploy.bat landlord_profile_name`
1. If on windows you may need zip.exe in your path http://stahlworks.com/dev/index.php?tool=zipunzip

# Beanstalk setup
1. North California
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
       * SAML_CERT - Single sign on identity provider certificate.  Found here for dev https://ssodev2.jpl.nasa.gov/oamfed/idp/metadata ```MIIB/jCCAWegAwIBAgIBCjANBgkqhkiG9w0BAQQFADAkMSIwIAYDVQQDExlkZWFvYW0tZGV2MDIuanBsLm5hc2EuZ292MB4XDTE2MDYzMDA0NTQxNloXDTI2MDYyODA0NTQxNlowJDEiMCAGA1UEAxMZZGVhb2FtLWRldjAyLmpwbC5uYXNhLmdvdjCBnzANBgkqhkiG9w0BAQEFAAOBjQAwgYkCgYEAht1N4lGdwUbl7YRyHwSCrnep6/e2I3+Veue0pSA/DGn8OuR/udM8UCja5utqlqJdq200ox4b4Mpz0Jg9kMckALtKe+1DgeESEIx9FpeuBdHlitYQNSbEr30HIG2nmeTOy4Vi5unBO54um3tNazcUTMA0/LJ6KQL8LeZSlB/IxwUCAwEAAaNAMD4wDAYDVR0TAQH/BAIwADAPBgNVHQ8BAf8EBQMDB9gAMB0GA1UdDgQWBBRYo1YjfrNonauLzj6/AsueWFGSszANBgkqhkiG9w0BAQQFAAOBgQACq7GHK/Zsg0+qC0WWa2ZjmOXE6Dqk/xuooG49QT7ihABs7k9U27Fw3xKF6MkC7pca1FwT82eZK1N3XKKpZe7Flu1fMKt2o/XSiBkDjWwUcChVnwGsUBe8hJFwFqg7olNJn1kaVBJUqZIiXF9kS0d+1H55rStOd0CNXAzp9utr2A==```
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

# Setup HTTPS
1. Login to JPL Certificate manager at https://ssl.jpl.nasa.gov
1. Manage my certificates, select landform.hi.jpl.nasa.gov, save this info for later 
1. Go to AWS Certificate Manager
1. Import a certificate
1. Paste Certificate content into Certificate body
1. Paste Intermediate and Root certificate into certifcation chian (include the stuff ---- but not the labels between each cert)
1. Paste the contents of \\opslab-central\project\landform\webcert\landform.hi.jpl.nasa.gov.key in the private key field
1. Import
1. Edit name tag to be landform.hi.jpl.nasa.gov
1. Go to elastic beanstalk Load Balancing configuration
1. Select the SSL certificate ID.  These may appear as *.jpl.nasa.gov, in which case you need to use debug console to inspect the drop down and compare the ARN with what is listed in the AWS certificate manager.  Then count how many items down in the option list it is and select that one.  Not kidding.  This is how it works.

# Test Procedures

### Authentication

SSO Test Procedures

1. Note this will only work on https://landform.hi.jpl.nasa.gov because that is the domain SSO is configured to use.
2. Click `API Token` and confirm the response is `Not Authenticated`
3. Click `Login (SSO)` 
4. Enter JPL credentials
5. Click `API Toke` and confirm the response is a web token - copy the token ID for later
6. Click `Logout`
7. Click `API Token` and confirm the response is `Not Authenticated`

LDAP Test Procedure

1. Note that this will not work on https://landform.hi.jpl.nasa.gov because it is running on a cloud instance and does not currently have access to JPLs internal network / LDAP server. However, it will work when the server is run locally or on a JPL host machine.
2. If your JPL user is not in the landform LDAP group contact Alex Menzies to be added
3. Click `Login (LDAP)` 
4. Enter JPL credentials
5. Click `API Token` and confirm the response is a web token - copy the token ID for later
6. Click `Logout`
7. Click `API Token` and confirm the response is `Not Authenticated`

REST API Test Procedure

1. Login and get an API token

2. Create a project
   `curl -d '{"name":"test-project-name"}' -H "Content-Type: application/json" -H "x-landform-token: API_TOKEN" -X POST https://landform.hi.jpl.nasa.gov/api/project`

3. List projects
   `curl -H "Content-Type: application/json" -H "x-landform-token: API_TOKEN" -X GET https://landform.hi.jpl.nasa.gov/api/project`

4. Get data about a project
   `curl -H "Content-Type: application/json" -H "x-landform-token: API_TOKEN" -X GET https://landform.hi.jpl.nasa.gov/api/project/test-project-name`

5. Delete a project
   `curl -H "Content-Type: application/json" -H "x-landform-token: API_TOKEN" -X DELETE https://landform.hi.jpl.nasa.gov/api/project/test-project-name`

   