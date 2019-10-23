# Landform Dev Team Settings

For development and integration testing use the `-dev` names, e.g. `landformweb-dev`, for production omit `-dev`.

* AWS console: http://goto.jpl.nasa.gov/awsconsole
* AWS region: `us-west-1`
* AWS account: `landlords/account_owner`
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

