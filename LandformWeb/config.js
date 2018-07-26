const path = require('path');

// JPL Dev SSO SAML endpoints and certs can be found here
// Metadata including SAML cert: https://ssodev2.jpl.nasa.gov/oamfed/idp/metadata
// SSO Service: https://ssodev2.jpl.nasa.gov/oamfed/idp/samlv20

const development = {
  app: {
    name: 'Landform',
    port: process.env.PORT || 8081,
    tokenSecret: process.env.TOKEN_SECRET || 'dogfishwaffelsandwitchwithbutter-43t-kdfvk',
    tokenHeader: 'x-landform-token',
    tokenCookie: 'landform-token',
    tokenLifespan: 60 * 60 * 24 * 10, //10 days in seconds
    sessionCookieSecret: process.env.SESSION_SECRET || 'iancnewowahasliuyfhenwlvncxhzuwyeoifjs',
    sessionCookieSecure: false,
    sessionTimeout: 1000 * 60 * 60 * 24, //24h in ms
    ldapGroup: 'landform',
    logLevel: process.env.LOG_LEVEL || 'silly', //error, warn, info, verbose, debug, silly
    uploadDir: process.env.UPLOAD_DIR || path.join(process.cwd(), 'upload'),
    binDir: process.env.BIN_DIR || path.join(process.cwd(), 'bin'),
    logDir: process.env.LOG_DIR || path.join(process.cwd(), 'log'),
    dbPrefix: process.env.DB_PREFIX || 'devtiles',
    awsProfile: process.env.AWS_PROFILE || 'default',
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
      path: process.env.SAML_PATH || '/sso/complete',
      entryPoint: process.env.SAML_ENTRY_POINT || 'https://ssodev2.jpl.nasa.gov/oamfed/idp/initiatesso?providerid=https://landform.hi.jpl.nasa.gov',
      issuer: 'passport-saml',
      cert: process.env.SAML_CERT || 'MIIB/jCCAWegAwIBAgIBCjANBgkqhkiG9w0BAQQFADAkMSIwIAYDVQQDExlkZWFvYW0tZGV2MDIuanBsLm5hc2EuZ292MB4XDTE2MDYzMDA0NTQxNloXDTI2MDYyODA0NTQxNlowJDEiMCAGA1UEAxMZZGVhb2FtLWRldjAyLmpwbC5uYXNhLmdvdjCBnzANBgkqhkiG9w0BAQEFAAOBjQAwgYkCgYEAht1N4lGdwUbl7YRyHwSCrnep6/e2I3+Veue0pSA/DGn8OuR/udM8UCja5utqlqJdq200ox4b4Mpz0Jg9kMckALtKe+1DgeESEIx9FpeuBdHlitYQNSbEr30HIG2nmeTOy4Vi5unBO54um3tNazcUTMA0/LJ6KQL8LeZSlB/IxwUCAwEAAaNAMD4wDAYDVR0TAQH/BAIwADAPBgNVHQ8BAf8EBQMDB9gAMB0GA1UdDgQWBBRYo1YjfrNonauLzj6/AsueWFGSszANBgkqhkiG9w0BAQQFAAOBgQACq7GHK/Zsg0+qC0WWa2ZjmOXE6Dqk/xuooG49QT7ihABs7k9U27Fw3xKF6MkC7pca1FwT82eZK1N3XKKpZe7Flu1fMKt2o/XSiBkDjWwUcChVnwGsUBe8hJFwFqg7olNJn1kaVBJUqZIiXF9kS0d+1H55rStOd0CNXAzp9utr2A==',
    },
  },
};

// override select values for production
const production = JSON.parse(JSON.stringify(development)); //deep copy
production.app.sessionCookieSecure = true;
production.app.logLevel = process.env.LOG_LEVEL || 'verbose';

module.exports = { development, production }[process.env.NODE_ENV || 'development'];
