const path = require('path');
const os = require('os');

const env = process.env;
const cwd = process.cwd();

const development = {
  app: {

    name: 'Landform',

    bundle: 'landformweb.zip',
    deployEnvironment: env.EB_DEPLOY_ENV || 'landformweb-dev',

    port: env.PORT || 8081,

    tokenSecret: env.TOKEN_SECRET || 'dogfishwaffelsandwitchwithbutter-43t-kdfvk',
    tokenHeader: 'x-landform-token',
    tokenCookie: 'landform-token',
    tokenLifespan: 60 * 60 * 24 * 10, //10 days in seconds

    sessionCookieSecret: env.SESSION_SECRET || 'iancnewowahasliuyfhenwlvncxhzuwyeoifjs',
    sessionCookieSecure: false, //no https for dev
    sessionTimeout: 1000 * 60 * 60 * 24, //24h in ms

    ldapGroup: env.TILE_SERVER_LDAP_GROUP || 'landform',

    logLevel: env.LOG_LEVEL || 'silly', //error, warn, info, verbose, debug, silly

    uploadDir: env.UPLOAD_DIR || path.join(cwd, 'upload'),
    binDir: env.BIN_DIR || path.join(cwd, '..', 'TilingServer', 'bin', !!env.DEBUG_TILING_SERVER ? 'Debug' : 'Release'),
    logDir: env.LOG_DIR || path.join(cwd, 'log'),
    tmpDir: env.TMP_DIR || path.join(cwd, 'tmp'),

    awsProfile: env.TILE_SERVER_PROFILE || env.AWS_PROFILE || 'default',
    awsRegion: env.TILE_SERVER_REGION || env.AWS_DEFAULT_REGION || 'us-west-1',

    venueName: env.TILE_SERVER_VENUE_NAME || `landformweb-dev-${os.userInfo().username}-${os.hostname()}`,
    s3URL: env.TILE_SERVER_S3_URL || 's3://landlords-dev',

    multiThreadedMaster: !!env.MULTI_THREADED_MASTER,

    reapOldTasks: 60 * 60 * 24, //24h in sec
  },
  ldap: {
    url: 'ldaps://ldap.jpl.nasa.gov',
    searchFilter: 'uid={{username}}',
    searchBase: 'ou=personnel,dc=dir,dc=jpl,dc=nasa,dc=gov',
    tlsOptions: {
      ciphers: 'RC4-MD5',
    },
  },
  sso: {
    strategy: 'saml',
    saml: {
      path: env.SAML_PATH || '/sso/complete',
      entryPoint: null, //SSO only works in integration or production environments
      issuer: 'passport-saml',
      cert: null,
    },
  },
};

// override select values for integration
const integration = JSON.parse(JSON.stringify(development)); //deep copy
integration.app.deployEnvironment = env.EB_DEPLOY_ENV || 'landformweb-dev';
integration.app.venueName = env.TILE_SERVER_VENUE_NAME || 'landformweb-dev';
integration.app.sessionCookieSecure = !env.WITHOUT_HTTPS;
integration.app.logLevel = env.LOG_LEVEL || 'verbose';
integration.app.binDir = env.BIN_DIR || path.join(cwd, 'bin');
integration.app.awsProfile = env.TILE_SERVER_PROFILE || env.AWS_PROFILE || 'null';
integration.sso.saml.entryPoint = env.SAML_ENTRY_POINT || 'https://ssoint.jpl.nasa.gov/oamfed/idp/initiatesso?providerid=https://landform.hi.jpl.nasa.gov';
integration.sso.saml.cert = env.SAML_CERT || 'MIIFpzCCBI+gAwIBAgIQCddb0dIGj+2dutdiIZYmAzANBgkqhkiG9w0BAQsFADBwMQswCQYDVQQGEwJVUzEVMBMGA1UEChMMRGlnaUNlcnQgSW5jMRkwFwYDVQQLExB3d3cuZGlnaWNlcnQuY29tMS8wLQYDVQQDEyZEaWdpQ2VydCBTSEEyIEhpZ2ggQXNzdXJhbmNlIFNlcnZlciBDQTAeFw0xNjA4MDQwMDAwMDBaFw0xOTA4MDkxMjAwMDBaMIGSMQswCQYDVQQGEwJVUzETMBEGA1UECBMKQ2FsaWZvcm5pYTEdMBsGA1UEBxMUTGEgQ2FuYWRhIEZsaW50cmlkZ2UxJzAlBgNVBAoTHk5BU0EgSmV0IFByb3B1bHNpb24gTGFib3JhdG9yeTENMAsGA1UECxMET0NJTzEXMBUGA1UEAwwOKi5qcGwubmFzYS5nb3YwggEiMA0GCSqGSIb3DQEBAQUAA4IBDwAwggEKAoIBAQCBtTxvISJpuHJ0A64Dcox3HwgtLAZEV1daTQavkrIpVLKc+B96Cg5lOSbvDloYXWrV/lVW091qfJODEmXXCi+Fe0T7a2+1aLHn40P4AAtxJSJc5uIbygtR97BTo8H8bNTl2gaeWJnm+vRJUapspT3elybPdAmsRWTECuXVR1RkoQTDOCoT3avM5cwnOl9e5PNGsfaj6a8AEWuEKR0eF+XeQa1sbYKT0+7qO6d0EfRaDpfz3BjkXHVBKV6Vy3bOkHsIiSVmzpCK6haeR8CvN4ZIAcTeiO0NmBekW9usgD3F73rk3DXjW0Lh+GPJMRqDpSH26CdrZt4qG4qYqUjq17I3AgMBAAGjggIYMIICFDAfBgNVHSMEGDAWgBRRaP+QrwIHdTzM2WVkYqISuFlyOzAdBgNVHQ4EFgQURMXWSGReNXi447vaB5+SFDbMAOQwSgYDVR0RBEMwQYIOKi5qcGwubmFzYS5nb3aCDGpwbC5uYXNhLmdvdoIhYWxsX2NlcnRpZmljYXRlcy5waHAuanBsLm5hc2EuZ292MA4GA1UdDwEB/wQEAwIFoDAdBgNVHSUEFjAUBggrBgEFBQcDAQYIKwYBBQUHAwIwdQYDVR0fBG4wbDA0oDKgMIYuaHR0cDovL2NybDMuZGlnaWNlcnQuY29tL3NoYTItaGEtc2VydmVyLWc1LmNybDA0oDKgMIYuaHR0cDovL2NybDQuZGlnaWNlcnQuY29tL3NoYTItaGEtc2VydmVyLWc1LmNybDBMBgNVHSAERTBDMDcGCWCGSAGG/WwBATAqMCgGCCsGAQUFBwIBFhxodHRwczovL3d3dy5kaWdpY2VydC5jb20vQ1BTMAgGBmeBDAECAjCBgwYIKwYBBQUHAQEEdzB1MCQGCCsGAQUFBzABhhhodHRwOi8vb2NzcC5kaWdpY2VydC5jb20wTQYIKwYBBQUHMAKGQWh0dHA6Ly9jYWNlcnRzLmRpZ2ljZXJ0LmNvbS9EaWdpQ2VydFNIQTJIaWdoQXNzdXJhbmNlU2VydmVyQ0EuY3J0MAwGA1UdEwEB/wQCMAAwDQYJKoZIhvcNAQELBQADggEBAJ/cCQdmRMKTXsvJgLhTY5tfVttPDyMa5tbwX0/fMVdcO68u6MN7NrLGY+KPM2K9D3LrSiSGbmmwk19IrRRUilFemq5nCOmve3LpPwoxlVajgagXiStR3W/+mwiidCQOCrl+hJVaNi3if6lUGnkh9/Iel30HUQnQTN84mnFFlVngeBMy20/21mzXkhDzFQRJAzXH83hgu37jygRm0BcZrKwYdw/4UfTf6V6hnEwjkj0j7Tw2tcrjQCjmv5/mz8fTKFW0lguqcd5PdaapwfCrbkbOmRYgeYUy6MZOpfWXwXchUkKaPKYoTYx7uxR28LGuNNIfsece5O14qnnO+JBJ7CY='; //from https://ssoint.jpl.nasa.gov/oamfed/idp/metadata

// override select values for production
const production = JSON.parse(JSON.stringify(integration)); //deep copy
production.app.deployEnvironment = env.EB_DEPLOY_ENV || 'landformweb';
production.app.venueName = env.TILE_SERVER_VENUE_NAME || 'landformweb';
production.sso.saml.entryPoint = env.SAML_ENTRY_POINT || 'https://sso1.jpl.nasa.gov/oamfed/idp/initiatesso?providerid=https://landform.hi.jpl.nasa.gov';
production.sso.saml.cert = env.SAML_CERT || 'MIIFpzCCBI+gAwIBAgIQDcsYOUUZNlOUbRdpbn9/njANBgkqhkiG9w0BAQsFADBwMQswCQYDVQQGEwJVUzEVMBMGA1UEChMMRGlnaUNlcnQgSW5jMRkwFwYDVQQLExB3d3cuZGlnaWNlcnQuY29tMS8wLQYDVQQDEyZEaWdpQ2VydCBTSEEyIEhpZ2ggQXNzdXJhbmNlIFNlcnZlciBDQTAeFw0xNjA4MDQwMDAwMDBaFw0xOTA4MDkxMjAwMDBaMIGSMQswCQYDVQQGEwJVUzETMBEGA1UECBMKQ2FsaWZvcm5pYTEdMBsGA1UEBxMUTGEgQ2FuYWRhIEZsaW50cmlkZ2UxJzAlBgNVBAoTHk5BU0EgSmV0IFByb3B1bHNpb24gTGFib3JhdG9yeTENMAsGA1UECxMET0NJTzEXMBUGA1UEAwwOKi5qcGwubmFzYS5nb3YwggEiMA0GCSqGSIb3DQEBAQUAA4IBDwAwggEKAoIBAQCc5h6tNlRHN+usRtSaDrHhCLmplmzvbOrD8Lfy164umvtmS6GZeYq+4zVuoASCUqo+stZ3n1KAjcNDcZ45eDHxPzMiiDlA94ZxfJGx/EJfr+tKh2uNp3ZGLaTJVFLqBKxxbppd/ox8Mw9tqqxnQn9KlEUHDhTMTQhDVAS/7pYUhgmqkxN2uxkBg8Q9CVeq3NffEI+uXgfJlbuZjcwe6a6LsXwZorQnTBCSR02dbRwC9s9FCZwyonhkqTbHTFSaxC3RYQBb3nau5wyI9cMsNjDQUvidjkIDO5jaFUd7ygdLTUu0HS2wRrTSx7nnpD6XtK/KukXEDivHI9/W6qWw5U9TAgMBAAGjggIYMIICFDAfBgNVHSMEGDAWgBRRaP+QrwIHdTzM2WVkYqISuFlyOzAdBgNVHQ4EFgQUyEoZBvKlfmr+xFuQ+iUl9E1QWC4wSgYDVR0RBEMwQYIOKi5qcGwubmFzYS5nb3aCDGpwbC5uYXNhLmdvdoIhYWxsX2NlcnRpZmljYXRlcy5waHAuanBsLm5hc2EuZ292MA4GA1UdDwEB/wQEAwIFoDAdBgNVHSUEFjAUBggrBgEFBQcDAQYIKwYBBQUHAwIwdQYDVR0fBG4wbDA0oDKgMIYuaHR0cDovL2NybDMuZGlnaWNlcnQuY29tL3NoYTItaGEtc2VydmVyLWc1LmNybDA0oDKgMIYuaHR0cDovL2NybDQuZGlnaWNlcnQuY29tL3NoYTItaGEtc2VydmVyLWc1LmNybDBMBgNVHSAERTBDMDcGCWCGSAGG/WwBATAqMCgGCCsGAQUFBwIBFhxodHRwczovL3d3dy5kaWdpY2VydC5jb20vQ1BTMAgGBmeBDAECAjCBgwYIKwYBBQUHAQEEdzB1MCQGCCsGAQUFBzABhhhodHRwOi8vb2NzcC5kaWdpY2VydC5jb20wTQYIKwYBBQUHMAKGQWh0dHA6Ly9jYWNlcnRzLmRpZ2ljZXJ0LmNvbS9EaWdpQ2VydFNIQTJIaWdoQXNzdXJhbmNlU2VydmVyQ0EuY3J0MAwGA1UdEwEB/wQCMAAwDQYJKoZIhvcNAQELBQADggEBAJvuK7wdLxXchjVYOVr+i+h88ea48brMatq8rq/AkQe7l4gsjmVhLYcj5cPGj+qHK2iGD45Ubaio/0alGuuJzPdkTx4sy78/5WSx5nex+hO4X+xKzRQRN+ziQtVulImOrTYjbhZgjyt1s2I8GOIJtpDa5M5Vl2vz4VSi0oKCCFamyeHCk0HfE5gMRcESRhuo4lQ8TFrpaLNNq21Tej22DqsmKCWhyt/EaEGirhEnWCfr4P+MLUB8HUzFALhMPwu67O/NSnd3UI8QLit+fcOvDGl0XDaIuv2jDeCKSNHGr+yglrJjA8p5G1Ethce208JtxeoDi63Zutbgi7LzscM4OYc='; //from https://sso1.jpl.nasa.gov/oamfed/idp/metadata

module.exports = { development, integration, production }[env.NODE_ENV || 'development'];
