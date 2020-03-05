const path = require('path');
const os = require('os');
const tls = require('tls');

const env = process.env;
const cwd = process.cwd();

const development = {
  app: {

    name: 'Landform',

    masterExe: 'TilingServer.exe',
    workerExe: 'TilingServer.exe',

    masterBundle: 'landform-master.zip',
    workerBundle: 'landform-worker.zip',
    deployEnvironment: env.EB_DEPLOY_ENV || 'landformweb-dev',

    port: env.PORT || 8081,

    tokenSecret: env.TOKEN_SECRET || 'dogfishwaffelsandwitchwithbutter-43t-kdfvk',
    tokenHeader: 'x-landform-token',
    tokenCookie: 'landform-token',
    tokenLifespan: 60 * 60 * 24 * 10, //10 days in seconds

    sessionCookieSecret: env.SESSION_SECRET || 'iancnewowahasliuyfhenwlvncxhzuwyeoifjs',
    sessionCookieSecure: false, //no https for dev
    sessionTimeout: 1000 * 60 * 60 * 24, //24h in ms

    ldapGroup: env.LANDFORM_LDAP_GROUP || 'landform',

    logLevel: env.LOG_LEVEL || 'silly', //error, warn, info, verbose, debug, silly

    uploadDir: env.UPLOAD_DIR || path.join(cwd, 'upload'),
    binDir: env.BIN_DIR || path.join(cwd, '..', 'TilingServer', 'bin', env.DEBUG_TILING_SERVER ? 'Debug' : 'Release'),
    logDir: env.LOG_DIR || path.join(cwd, 'log'),
    tmpDir: env.TMP_DIR || path.join(cwd, 'tmp'),

    //default venue name same as in ConfigureCloud.cs
    venue: env.LANDFORM_VENUE || `landform-dev-${os.userInfo().username}-${os.hostname()}`,
    s3Url: env.LANDFORM_S3_URL || 's3://landlords-dev/landform-web',
    awsRegion: env.LANDFORM_AWS_REGION || env.AWS_DEFAULT_REGION || 'us-west-1',
    awsProfile: env.LANDFORM_AWS_PROFILE || env.AWS_PROFILE || 'default',

    //TODO MSL specific
    MSLICEAWSProfile: env.LANDFORM_MSLICE_AWS_PROFILE || 'mslice',
    MSLICEAWSRegion: env.LANDFORM_MSLICE_AWS_REGION || 'us-west-1',
    MSLICES3Url: env.LANDFORM_MSLICE_S3_URL || 's3://red-product',

    reapOldTasks: 60 * 60 * 24, //24h in sec
  },
  ldap: {
    reconnect: true,
    url: 'ldaps://ldap.jpl.nasa.gov',
    searchFilter: 'uid={{username}}',
    searchBase: 'ou=personnel,dc=dir,dc=jpl,dc=nasa,dc=gov',
    tlsOptions: {
      secureContext: tls.createSecureContext({ secureProtocol: 'TLSv1_method' }),
    },
  },
  sso: {
    strategy: 'saml',
    saml: {
      path: env.SAML_PATH || '/sso/complete',
      entryPoint: null, //SSO only works in integration or production environments
      issuer: 'passport-saml',
      cert: 'none', //cannot be null or empty
    },
  },
};

//work with Mark Nastri to update SSO configuration
//mark.a.nastri@jpl.nasa.gov
//JPL wiki page for Landform SSO configuration: https://wiki.jpl.nasa.gov/pages/viewpage.action?pageId=186145545

// override select values for integration
const integration = JSON.parse(JSON.stringify(development)); //deep copy
integration.app.deployEnvironment = env.EB_DEPLOY_ENV || 'landformweb-dev';
integration.app.venue = env.LANDFORM_VENUE || 'landformweb-dev';
integration.app.sessionCookieSecure = !env.WITHOUT_HTTPS;
integration.app.binDir = env.BIN_DIR || path.join(cwd, 'bin');
integration.app.awsProfile = env.LANDFORM_AWS_PROFILE || env.AWS_PROFILE || 'null';
integration.sso.saml.entryPoint = env.SAML_ENTRY_POINT || 'https://ssoint.jpl.nasa.gov/oamfed/idp/initiatesso?providerid=https://landform.hi.jpl.nasa.gov';
integration.sso.saml.cert = env.SAML_CERT || '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'; //dsig:X509Certificate from https://ssoint.jpl.nasa.gov/oamfed/idp/metadata

// override select values for production
const production = JSON.parse(JSON.stringify(integration)); //deep copy
production.app.deployEnvironment = env.EB_DEPLOY_ENV || 'landformweb';
production.app.venue = env.LANDFORM_VENUE || 'landformweb';
integration.app.logLevel = env.LOG_LEVEL || 'verbose';
production.sso.saml.entryPoint = env.SAML_ENTRY_POINT || 'https://sso1.jpl.nasa.gov/oamfed/idp/initiatesso?providerid=https://landform.hi.jpl.nasa.gov';
production.sso.saml.cert = env.SAML_CERT || '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'; //dsig:X509Certificate from https://sso1.jpl.nasa.gov/oamfed/idp/metadata

module.exports = { development, integration, production }[env.NODE_ENV || 'development'];
