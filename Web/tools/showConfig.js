const config = require('../config');

console.log(`NODE_ENV: ${process.env.NODE_ENV || 'development'}`);
console.log(`LDAP group: ${config.app.ldapGroup}`);
console.log(`Landform venue: ${config.app.venueName}`);
console.log(`S3 URL: ${config.app.s3Url}`);
console.log(`AWS Region: ${config.app.awsRegion}`);
console.log(`AWS Profile: ${config.app.awsProfile}`);
console.log(`AWS MSLICE Profile: ${config.app.awsMSLICEProfile}`);
console.log(`AWS MSLICE S3 URL: ${config.app.awsMSLICES3Url}`);
