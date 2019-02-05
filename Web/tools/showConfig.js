const config = require('../config');

console.log(`NODE_ENV: ${process.env.NODE_ENV || 'development'}`);
console.log(`LDAP group: ${config.app.ldapGroup}`);
console.log(`Landform venue: ${config.app.venue}`);
console.log(`S3 URL: ${config.app.s3Url}`);
console.log(`AWS region: ${config.app.awsRegion}`);
console.log(`AWS profile: ${config.app.awsProfile}`);
console.log(`MSLICE AWS profile: ${config.app.MSLICEAWSProfile}`);
console.log(`MSLICE S3 URL: ${config.app.MSLICES3Url}`);
