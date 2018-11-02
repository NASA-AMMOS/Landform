#!/bin/sh
datadir=/mnt/landformweb-test-data
srcdir=/home/ubuntu/landformweb
curl -sL https://deb.nodesource.com/setup_8.x | bash -
apt-get install -y s3fs nodejs make unzip
umount $datadir # ok if doesn't exist or not mounted
mkdir -p $datadir
chmod 777 $datadir
s3fs landlords-dev:/landformweb-test-data $datadir -o iam_role -o allow_other 
cd /home/ubuntu
npm install -g npm@5.6.0
chown -R ubuntu .config
sudo -u ubuntu mkdir -p $srcdir
sudo -u ubuntu unzip -o $datadir/landformweb.zip -d $srcdir
cd $srcdir
#normal package install fails by default for several reasons
#one reason is that verdaccio.jws.jpl.nasa.gov is unreachable - this could prob be fixed by removing the @jpl packages from package.json
#another reason is that it crashes with ENOENT errors for stuff in .staging - this seems to be fixed by removing package-lock.json
#sudo -u ubuntu npm install 
#but for now just ignore the normal package setup and install the few needed packages directly
sudo -u ubuntu mv package.json package.json-NOT
sudo -u ubuntu mv package-lock.json package-lock.json-NOT
sudo -u ubuntu npm install newman fs-extra json-parse-better-errors strip-json-comments
# run tests once per day at minute 0 of hour 7 UTC
# this is midnight pacific time during daylight saving time (summer)
# and 11pm pacific time otherwise
(crontab -u ubuntu -l ; echo "0 7 * * * node $srcdir/tools/runTests.js $datadir") | sort - | uniq - | crontab -u ubuntu -
