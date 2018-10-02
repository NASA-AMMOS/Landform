#!/usr/bin/env python

# this script is compatible with python 2 and 3

# quickstart
#
# 1) install latest python3
#
# 2) pip install boto beautifulsoup4 requests-ntlm
#    pip install awscli --upgrade --user # if you want to use aws CLI
#    pip install awsebcli --upgrade --user # if you want to use elastic beanstalk CLI
#
# 3) your PATH environment variable must include
#
#    Windows: %USERPROFILE%\AppData\Roaming\Python\Python??\Scripts
#    Linux: ~/.local/bin
#    MacOS: ~/Library/Python/?.?/bin
#
# 4) open windows command prompt (not git bash or cygwin) and run
#
#    aws-login.py
#
#    or in git bash run
#
#    winpty python aws-login.py
#
# 5) enter JPL username and password when prompted
#
#    NOTE: this will create %HOME%\.aws\credentials, which is correct
#    even if you have specialized HOME to be different from %USERPROFILE%
#    e.g. in my case HOME=c:\cygwin64\home\ME but USERPROFILE=c:\users\ME
#    however, watch out for programs such as NPM which ignore custom setting of HOME
#    in that case, you may need to use mklink to create a symlink USERPROFILE/.aws -> HOME/.aws
#
# 6) try some stuff, e.g.
#
#    aws ec2 describe-instances
#    aws s3 ls
#
#    if output_profile is not 'default' then you will need to add a --profile option, e.g.
#
#    aws --profile saml s3 ls

import sys
import os
import boto.sts
import boto.s3
import requests
import getpass
import base64
import logging
import xml.etree.ElementTree as ET
import re
from bs4 import BeautifulSoup
from os.path import expanduser

if (sys.version_info > (3, 0)):
    import configparser as ConfigParser
    from urllib.parse import urlparse, urlunparse
    from builtins import input as raw_input
else:
    import ConfigParser
    from urlparse import urlparse, urlunparse

#uncomment this for lots of debug spew
#boto.set_stream_logger('boto')

##########################################################################
# Variables

# awsconfigfile: The file where this script will update with the new credentials
awsconfigfile = '/.aws/credentials'

# region: The AWS region that this script will connect to for all API calls
# this is also written to the output config file
region = 'us-west-1'

# output profile: the section to modify in the AWS config file
#output_profile = 'saml'
output_profile = 'default'

# output format: The AWS CLI output format that will be configured in the
# output profile (affects subsequent CLI calls)
output_format = 'json'

# SSL certificate verification: Whether or not strict certificate
# verification is done, False should only be used for dev/test
sslverification = True

# idpentryurl: The initial url that starts the authentication process.
#idpentryurl = 'https://sso2.jpl.nasa.gov:443/adfs/ls/IdpInitiatedSignOn.aspx?loginToRp=urn:amazon:webservices'
idpentryurl = 'https://sso2.jpl.nasa.gov/adfs/ls/IdpInitiatedSignOn.aspx?loginToRp=urn:amazon:webservices'

# active directory / LDAP domain to prepend to username
# empty for none
ad_domain = 'JPL'

# the credentials will be valid for this many seconds
# this cannot exceed the maximum configured for the role
#duration = 3600 # 1h
duration = 14400 # 4h

# Uncomment to enable low level debugging
#logging.basicConfig(level=logging.DEBUG)

##########################################################################
# Get the federated credentials from the user
sys.stdout.write('Username:') # python2/3 compat print w/o newline
sys.stdout.flush()            
username = raw_input()
password = getpass.getpass()
print('')

# Initiate session handler
session = requests.Session()

# Programmatically get the SAML assertion
# Opens the initial IdP url and follows all of the HTTP302 redirects, and
# gets the resulting login page
formresponse = session.get(idpentryurl, verify=sslverification)
# Capture the idpauthformsubmiturl, which is the final url after all the 302s
idpauthformsubmiturl = formresponse.url

# Parse the response and extract all the necessary values
# in order to build a dictionary of all of the form values the IdP expects
formtxt = formresponse.text
try: formtxt = formtxt.decode('utf8')
except AttributeError: pass
#formsoup = BeautifulSoup(formtxt)
formsoup = BeautifulSoup(formtxt, 'html.parser')
payload = {}

for inputtag in formsoup.find_all(re.compile('(INPUT|input)')):
    name = inputtag.get('name','')
    value = inputtag.get('value','')
    if 'user' in name.lower():
        #Make an educated guess that this is the right field for the username
        if ad_domain: payload[name] = ad_domain + '\\' + username
        else: payload[name] = username
    elif 'email' in name.lower():
        #Some IdPs also label the username field as 'email'
        payload[name] = username
    elif 'pass' in name.lower():
        #Make an educated guess that this is the right field for the password
        payload[name] = password
    else:
        #Simply populate the parameter with the existing value (picks up hidden fields in the login form)
        payload[name] = value

# Debug the parameter payload if needed
# Use with caution since this will print sensitive output to the screen
#print(payload)

# Some IdPs don't explicitly set a form action, but if one is set we should
# build the idpauthformsubmiturl by combining the scheme and hostname 
# from the entry url with the form action target
# If the action tag doesn't exist, we just stick with the 
# idpauthformsubmiturl above
for inputtag in formsoup.find_all(re.compile('(FORM|form)')):
    action = inputtag.get('action')
    loginid = inputtag.get('id')
    if (action and loginid == 'loginForm'):
        parsedurl = urlparse(idpentryurl)
        idpauthformsubmiturl = parsedurl.scheme + '://' + parsedurl.netloc + action

# Performs the submission of the IdP login form with the above post data
response = session.post(idpauthformsubmiturl, data=payload, verify=sslverification)

# Debug the response if needed
#print(response.text)

# Overwrite and delete the credential variables, just for safety
username = '##############################################'
password = '##############################################'
del username
del password

# Decode the response and extract the SAML assertion
resptxt = response.text
try: resptxt = resptxt.decode('utf8')
except AttributeError: pass
#soup = BeautifulSoup(resptxt);
soup = BeautifulSoup(resptxt, 'html.parser')
assertion = ''

# Look for the SAMLResponse attribute of the input tag (determined by
# analyzing the debug print lines above)
for inputtag in soup.find_all('input'):
    if(inputtag.get('name') == 'SAMLResponse'):
        assertion = inputtag.get('value')

# Better error handling is required for production use.
if (assertion == ''):
    #TODO: Insert valid error checking/handling
    print('Response did not contain a valid SAML assertion')
    sys.exit(0)

# Debug only
#print(base64.b64decode(assertion))

# Parse the returned assertion and extract the authorized roles
awsroles = []
root = ET.fromstring(base64.b64decode(assertion))
for saml2attribute in root.iter('{urn:oasis:names:tc:SAML:2.0:assertion}Attribute'):
    if (saml2attribute.get('Name') == 'https://aws.amazon.com/SAML/Attributes/Role'):
        for saml2attributevalue in saml2attribute.iter('{urn:oasis:names:tc:SAML:2.0:assertion}AttributeValue'):
            awsroles.append(saml2attributevalue.text)

# Note the format of the attribute value should be role_arn,principal_arn
# but lots of blogs list it as principal_arn,role_arn so let's reverse
# them if needed
for awsrole in awsroles:
    chunks = awsrole.split(',')
    if 'saml-provider' in chunks[0]:
        newawsrole = chunks[1] + ',' + chunks[0]
        index = awsroles.index(awsrole)
        awsroles.insert(index, newawsrole)
        awsroles.remove(awsrole)

# If I have more than one role, ask the user which one they want,
# otherwise just proceed
print('')
if len(awsroles) > 1:
    i = 0
    print('Please choose the role you would like to assume:')
    for awsrole in awsroles:
        print('[{0}]: {1}'.format(i, awsrole.split(',')[0]))
        i += 1
    sys.stdout.write('Selection:') # python2/3 compat print w/o newline 
    sys.stdout.flush()            
    selectedroleindex = raw_input()

    # Basic sanity check of input
    if int(selectedroleindex) > (len(awsroles) - 1):
        print('You selected an invalid role index, please try again')
        sys.exit(0)

    role_arn = awsroles[int(selectedroleindex)].split(',')[0]
    principal_arn = awsroles[int(selectedroleindex)].split(',')[1]
else:
    role_arn = awsroles[0].split(',')[0]
    principal_arn = awsroles[0].split(',')[1]

# Use the assertion to get an AWS STS token using Assume Role with SAML
conn = boto.sts.connect_to_region(region, anon=True)
token = conn.assume_role_with_saml(role_arn, principal_arn, assertion, duration_seconds=duration)

# Write the AWS STS token into the AWS credential file
home = expanduser('~')
filename = home + awsconfigfile

# Update the config file
config = ConfigParser.RawConfigParser()
config.read(filename)

if not config.has_section(output_profile) and not output_profile == ConfigParser.DEFAULTSECT:
    config.add_section(output_profile)

config.set(output_profile, 'output', output_format)
config.set(output_profile, 'region', region)
config.set(output_profile, 'aws_access_key_id', token.credentials.access_key)
config.set(output_profile, 'aws_secret_access_key', token.credentials.secret_key)
config.set(output_profile, 'aws_session_token', token.credentials.session_token)

dirname = os.path.dirname(filename)
if not os.path.exists(dirname):
    os.makedirs(dirname)
with open(filename, 'w+') as configfile:
    config.write(configfile)

# Give the user some basic info as to what has just happened
print('\n\n----------------------------------------------------------------')
print('Your new access key pair has been stored in the AWS configuration file {0} under the "{1}" profile.'
      .format(filename, output_profile))
print('Note that it will expire at {0}.'.format(token.credentials.expiration))
print('After this time, you may safely rerun this script to refresh your access key pair.')
print('To use this credential, call the AWS CLI like this: "aws{0} ec2 describe-instances"'
      .format(' --profile ' + output_profile if output_profile != ConfigParser.DEFAULTSECT else ''))
print('----------------------------------------------------------------\n\n')

# Use the AWS STS token to list all of the S3 buckets
s3conn = boto.s3.connect_to_region(region,
                                   aws_access_key_id=token.credentials.access_key,
                                   aws_secret_access_key=token.credentials.secret_key,
                                   security_token=token.credentials.session_token)

buckets = s3conn.get_all_buckets()

print('Simple API example listing all S3 buckets:')
print(buckets)
