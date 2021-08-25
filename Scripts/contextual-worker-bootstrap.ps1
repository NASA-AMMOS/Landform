New-Partition -DiskNumber 1 -UseMaximumSize -IsActive -DriveLetter E | Format-Volume -FileSystem NTFS -NewFileSystemLabel landform-storage
$instanceId = (invoke-webrequest http://169.254.169.254/latest/meta-data/instance-id -UseBasicParsing).content
aws --region us-gov-west-1 ec2 attach-volume --volume-id vol-022b586b5117e7521 --instance-id $instanceId --device xvdf
