const path = require('path');

// JPL Dev SSO SAML endpoints and certs can be found here
// Metadata including SAML cert: https://ssodev2.jpl.nasa.gov/oamfed/idp/metadata
// SSO Service: https://ssodev2.jpl.nasa.gov/oamfed/idp/samlv20

const env = process.env;
const cwd = process.cwd();

const development = {
  app: {

    name: 'Landform',

    bundle: 'landformweb.zip',
    ebDeployEnvironment: env.EB_DEPLOY_ENV || 'landformweb', //or landformweb-dev
    localDeployTag: 'landformweb', //docker image tag for local deployment

    port: env.PORT || 8081,

    tokenSecret: env.TOKEN_SECRET || 'dogfishwaffelsandwitchwithbutter-43t-kdfvk',
    tokenHeader: 'x-landform-token',
    tokenCookie: 'landform-token',
    tokenLifespan: 60 * 60 * 24 * 10, //10 days in seconds

    sessionCookieSecret: env.SESSION_SECRET || 'iancnewowahasliuyfhenwlvncxhzuwyeoifjs',
    sessionCookieSecure: false,
    sessionTimeout: 1000 * 60 * 60 * 24, //24h in ms

    ldapGroup: 'landform',

    logLevel: env.LOG_LEVEL || 'silly', //error, warn, info, verbose, debug, silly

    uploadDir: env.UPLOAD_DIR || path.join(cwd, 'upload'),
    binDir: env.BIN_DIR || path.join(cwd, 'bin'),
    logDir: env.LOG_DIR || path.join(cwd, 'log'),
    tmpDir: env.TMP_DIR || path.join(cwd, 'tmp'),

    awsProfile: env.TILE_SERVER_PROFILE || env.AWS_PROFILE || 'default',
    awsRegion: env.TILE_SERVER_REGION || env.AWS_DEFAULT_REGION || 'us-west-1',

    venueName: env.TILE_SERVER_VENUE_NAME || 'webdevtiles',
    s3URL: env.TILE_SERVER_S3_URL || 's3://landlords-dev',

    singleThreadedMaster: env.SINGLE_THREADED_MASTER || 'false',

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
      entryPoint: env.SAML_ENTRY_POINT || 'https://ssodev2.jpl.nasa.gov/oamfed/idp/initiatesso?providerid=https://landform.hi.jpl.nasa.gov',
      issuer: 'passport-saml',
      cert: env.SAML_CERT || 'MIIB/jCCAWegAwIBAgIBCjANBgkqhkiG9w0BAQQFADAkMSIwIAYDVQQDExlkZWFvYW0tZGV2MDIuanBsLm5hc2EuZ292MB4XDTE2MDYzMDA0NTQxNloXDTI2MDYyODA0NTQxNlowJDEiMCAGA1UEAxMZZGVhb2FtLWRldjAyLmpwbC5uYXNhLmdvdjCBnzANBgkqhkiG9w0BAQEFAAOBjQAwgYkCgYEAht1N4lGdwUbl7YRyHwSCrnep6/e2I3+Veue0pSA/DGn8OuR/udM8UCja5utqlqJdq200ox4b4Mpz0Jg9kMckALtKe+1DgeESEIx9FpeuBdHlitYQNSbEr30HIG2nmeTOy4Vi5unBO54um3tNazcUTMA0/LJ6KQL8LeZSlB/IxwUCAwEAAaNAMD4wDAYDVR0TAQH/BAIwADAPBgNVHQ8BAf8EBQMDB9gAMB0GA1UdDgQWBBRYo1YjfrNonauLzj6/AsueWFGSszANBgkqhkiG9w0BAQQFAAOBgQACq7GHK/Zsg0+qC0WWa2ZjmOXE6Dqk/xuooG49QT7ihABs7k9U27Fw3xKF6MkC7pca1FwT82eZK1N3XKKpZe7Flu1fMKt2o/XSiBkDjWwUcChVnwGsUBe8hJFwFqg7olNJn1kaVBJUqZIiXF9kS0d+1H55rStOd0CNXAzp9utr2A==',
    },
  },
};

// override select values for production
const production = JSON.parse(JSON.stringify(development)); //deep copy
production.app.sessionCookieSecure = true;
production.app.logLevel = env.LOG_LEVEL || 'verbose';
production.app.awsProfile = env.TILE_SERVER_PROFILE || env.AWS_PROFILE || 'null';

module.exports = { development, production }[env.NODE_ENV || 'development'];
