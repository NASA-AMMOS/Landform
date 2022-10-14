rem this mimics packer-landform-ami-pipeline/playbook.yml
rem but for fast iterating on dev

set ver=2.7.0

set upload_path=c:\upload
set landform_package_version=Landform-%ver%.zip
set landform_vfw_dlls=vfw-dlls.zip
set landform_dist_url=s3://m20-ids-g-landform/deploy

rem manual bootstrap
rem mkdir c:\upload
rem cd c:\upload
rem curl https://awscli.amazonaws.com/AWSCLIV2.msi -o awscli.msi
rem msiexec /i awscli.msi /q
rem aws configure set region us-gov-west-1
rem aws s3 cp %landform_dist_url%/configure-ami.bat configure-ami.bat
rem configure-ami.bat

rem mkdir %upload_path%
rem curl https://awscli.amazonaws.com/AWSCLIV2.msi -o %upload_path%\awscli.msi
rem msiexec /i %upload_path%\awscli.msi /q
rem aws configure set region us-gov-west-1

mkdir c:\landform

mkdir c:\landform\config_files
aws s3 cp %landform_dist_url%/set-disk.ps1 c:\landform\config_files
aws s3 cp %landform_dist_url%/cloudwatch-bootstrap.ps1 c:\landform\config_files

aws s3 cp %landform_dist_url%/%landform_package_version% %upload_path%
aws s3 cp %landform_dist_url%/%landform_vfw_dlls% %upload_path%

curl https://community.chocolatey.org/install.ps1 -o %upload_path%\chocolatey-install.ps1
powershell %upload_path%\chocolatey-install.ps1

rem sadly the rest of this seems to require a reboot so that choco is in path...

choco install vim  -y
choco install 7zip -y

7z x %upload_path%\%landform_package_version% -oc:\landform
7z x %upload_path%\%landform_vfw_dlls% -oc:\Windows\System32

choco install vcredist-all -y

curl https://s3.amazonaws.com/amazoncloudwatch-agent/windows/amd64/latest/amazon-cloudwatch-agent.msi -o %upload_path%\amazon-cloudwatch-agent.msi
msiexec /i %upload_path%\amazon-cloudwatch-agent.msi /q

reg add HKLM\SYSTEM\CurrentControlSet\Control\FileSystem\LongPathsEnabled /t REG_DWORD /d 1 /f

rd /s /q c:\tmp
rd /s /q %upload_path%
rd /s /q c:\Users\Administrator\.aws

c:\ProgramData\Amazon\EC2-Windows\Launch\Settings\EC2LaunchSettings.exe

rem put this in userdata before running landform during release engineering
rem to avoid having to keep building a new AMI
rem replace A.B.C with the release version
rem also replace IAM role in launch template with am-m20-ids-s3-access
rem mkdir c:\upload
rem aws s3 cp s3://m20-ids-g-landform/deploy/Landform-A.B.C.zip c:\upload
rem 7z x c:\upload\Landform-A.B.C.zip -oc:\landform -aoa
