# Landform Dev Team Settings

For development and integration testing use the `-dev` names, e.g. `landformweb-dev`, for production omit `-dev`.

* AWS console:
    * if using AWS account "landlords" (589270964471) in us-west-1 (pubcloud): http://goto.jpl.nasa.gov/awsconsole
    * if using AWS account "m2020-dev-gov" (017717573760) in us-gov-west-1 (govcloud): http://goto.jpl.nasa.gov/govcloudconsole
    * if using AWS account "mipl-dev" (105249944904) in us-gov-west-1 (govcloud): http://goto.jpl.nasa.gov/govcloudconsole
* AWS region: `us-west-1` or `us-gov-west-1`
* AWS account: `landlords/account_owner`, `m2020-dev-gov/m20-dev-ids`, or `mipl-dev/power_user`
* S3 bucket: `landlords-dev`
* Elastic Beanstalk application name: `Landform`
* Elastic Beanstalk environment name: `landformweb[-dev]`
* Elastic Beanstalk IAM instance profile: `landlords`
* DNS provider: Amazon Route 53
* AWS account for Route 53: `jeffnorris/power_user`, `us-west-1`
* DNS name: `landform[-dev].hi.jpl.nasa.gov` -> `https://landformweb[-dev].us-west-1.elasticbeanstalk.com`
* HTTPS certificate information:
    * Country name: `US`
    * State name: `California`
    * Locality name: `Pasadena`
    * Organization name: `NASA Jet Propulsion Laboratory`
    * Organizational unit name: `OCIO`
    * Common name: `landform[-dev].hi.jpl.nasa.gov`
    * Email address: (empty)
    * Challenge password: (empty)
    * Company name: (empty)
  For internal Landform use we keep an archive of the HTTPS key, CSR, and PEM files, so be sure to discuss with the team before creating new ones.
* LDAP group: `landform`
* Environment variables
    * `NODE_ENV`: `production` (`integration` for testing)
        * setting `NODE_ENV` should be sufficient for internal Landform use as all other environment variables will be set automatically based on that
        * otherwise, use `NODE_ENV=production` and customize the variables below
    * `SESSION_SECRET`: any private string
    * `TOKEN_SECRET`: any private string
    * `SAML_ENTRY_POINT`: for internal Landform use SSO should already be properly configured.  Otherwise you will need to contact JPL IT to set up SSO for your deployment.  Typically this field will have a form like `https://SSO_HOST.jpl.nasa.gov/oamfed/idp/initiatesso?providerid=LANDFORM_URL` where `SSO_HOST` is `ssoint` for integration testing and `sso1` for production, and `LANDFORM_URL` is the URL to your Landform master server.
    * `SAML_CERT`: for internal Landform use SSO should already be properly configured depending on `NODE_ENV`.  Otherwise typically copy the X509Certificate field from `https://SSO_HOST.jpl.nasa.gov/oamfed/idp/metadata`.
    * `LANDFORM_AWS_REGION`: `us-west-1`
    * `LANDFORM_AWS_PROFILE`: `default`
    * `LANDFORM_VENUE`: `landformweb[-dev]` (recommended, but can be any name) - this is the Landform venue name for the deployment; it must match the venue name in the Landform worker configuration.
    * `LANDFORM_S3_URL`: `s3://landlords-dev` (internal landform use only, otherwise use your own S3 bucket)
    * `LANDFORM_MSLICE_S3_URL`: `s3://red-product`
    * `LANDFORM_LDAP_GROUP`: `landform` (internal landform use only, otherwise use your own LDAP group)
* EC2 key pair: `landform-ec2`
* EC2 worker launch template name: `landformweb[-dev]-workers`
* EC2 worker launch template name tag: `landformweb[-dev]-worker`
* EC2 worker launch template IAM instance profile: `landlords`
* EC2 auto scaling group name: `landformweb[-dev]-workers`

## AWS Credentials

Most development and deployment tasks require AWS credentials.

In most cases your JPL username needs to be added to an LDAP group to enable you to login to an AWS account.  For example, for the "landlords" AWS account 589270964471 the LDAP group is aws.589270964471.account_owner.  If you are not already in the LDAP group, you need to be added.

* for "landlords" account 589270964471 talk to Parker Abercrombie or Kevin Reeves
* for "m2020-dev-gov" account 017717573760 talk to Reynaldo Lopez-Roig or Jordan Lei
* for "mipl-dev" account 105249944904 talk to Jeff Liu

Once you are added to the appropriate LDAP group you should be able to acquire temporary credentials using the [credss.exe](https://github.jpl.nasa.gov/CS3/credss) application from CS3.

* for "landlords" (max 4h token)
    ```
    export LANDFORM_AWS_REGION=us-west-1
    export LANDFORM_AWS_ACCOUNT=589270964471
    export LANDFORM_AWS_ROLE=account_owner
    export LANDFORM_AWS_ENV=pub
    export LANDFORM_AWS_PROFILE=credss-landlords
    export LANDFORM_AWS_DURATION=14400
    export LANDFORM_S3_URL=s3://landlords-dev/landform-web
    ```
* for "m2020-dev-gov" (max 8h token)
    ```
    export LANDFORM_AWS_REGION=us-gov-west-1
    export LANDFORM_AWS_ACCOUNT=017717573760
    export LANDFORM_AWS_ROLE=m20-dev-ids
    export LANDFORM_AWS_ENV=gov
    export LANDFORM_AWS_PROFILE=credss-m2020-dev
    export LANDFORM_AWS_DURATION=28800
    export LANDFORM_S3_URL=s3://m20-ids-g-landform/landform-web
    ```
* for "mipl-dev" (max 4h token)
    ```
    export LANDFORM_AWS_REGION=us-gov-west-1
    export LANDFORM_AWS_ACCOUNT=105249944904
    export LANDFORM_AWS_ROLE=power_user
    export LANDFORM_AWS_ENV=gov
    export LANDFORM_AWS_PROFILE=credss-mipl-dev
    export LANDFORM_AWS_DURATION=14400
    export LANDFORM_S3_URL=s3://mipl-dev-landform/landform-web
    ```

We typically have a copy of `credss.exe` checked in under the `Utils` folder which is a sibling of `Web`.  You need to prefix the entire command line with `winpty` if using git bash to get password input to work.

```
cd Landform/Web
winpty ../Utils/credss.exe --region $LANDFORM_AWS_REGION --account $LANDFORM_AWS_ACCOUNT --role $LANDFORM_AWS_ROLE --env $LANDFORM_AWS_ENV --section $LANDFORM_AWS_PROFILE --aws-only --duration $LANDFORM_AWS_DURATION
```

This will generate temporary AWS credentials in `$HOME/.aws/credentials`.

## Nightly Test
`landform-test.hi.jpl.nasa.gov` is a `t3.small` EC2 instance running Ubuntu Server 18.04 LTS.  It runs the tests defined in [test/data/landform-test-config.json](test/data/landform-test-config.json) nightly using the [tools/runTests.js](toosl/runTests.js) script.  The landform sever it connects to is also specified in the test config file.  It reads the test data from S3 at `/landlords-dev/landformweb-test-data` using [s3fs-fuse](https://github.com/s3fs-fuse/s3fs-fuse) and writes timestamped log files back to the same directory tree in S3.  The tests are run serially in order starting at 7am UTC (11pm Pacific standard time, 12am Pacific daylight time).  If a test fails then one of the timestamped log files written to its directory will be named like `log-*-fail.txt`.  Failure of a test does not disable running subsequent tests.

The test projects may be visualized by logging in to the landform server, e.g. https://landform.hi.jpl.nasa.gov, and then going to a URL like https://landform.hi.jpl.nasa.gov/api/projects/PROJECT_NAME/view where `PROJECT_NAME` is the name of one of the test projects, e.g. `00-stick`.  The test results are available until the next round of tests are run.

The EC2 instance for running tests is created from an EC2 launch configuration named `landformweb-test` in the `landlords` account.  The userdata startup script is [test/data/ec2userdata-ubuntu-18.04.sh](test/data/ec2userdata-ubuntu-18.04.sh).  The DNS registration for `landform-test.hi.jpl.nasa.gov` is manually managed with Amazon Route 53.  See the [AWS setup](docs/SETUP.md) for info on that.  For maintenance you can ssh into the instance using the `landform-ec2` key pair like this:

    ssh -i path/to/landform-ec2 ubuntu@landform-test.hi.jpl.nasa.gov

(If you get an error about permissions on the key file then run `chmod 400 path/to/landform-ec2`.)  The instance has a firewall configured to allow SSH access only from JPL IP addresses - if you are remoted in over VPN then use full tunnel.

In order for `landform-test.hi.jpl.nasa.gov` to connect to a landform web server, e.g. `landform.hi.jpl.nasa.gov`, the latter must allow inbound HTTPS traffic from the former.  In our typical setup this means that the EC2 security group used by Elastic Beanstalk for the landform web server must have an entry manually added to it with `Type=HTTPS, Protocol=TCP, Port Range=443, Source=ADDR/32, Description=landform-test.hi.jpl.nasa.gov` where `ADDR` is the IP address of landform-test.hi.jpl.nasa.gov. Normally the security group is configured to allow incoming connections only from JPL IP addresses.  See the [AWS setup](docs/SETUP.md#5-restrict-to-jpl-ips) docs for more info.

