# mount NVMe ephemeral volume instance as E:\
# will be used as landform storage and temp
Set-Disk 1 -isOffline $false
Initialize-Disk 1 -PartitionStyle GPT
New-Partition -DiskNumber 1 -UseMaximumSize -DriveLetter E
Format-Volume -DriveLetter E -FileSystem NTFS -NewFileSystemLabel landform-storage

# mount persistent EBS volume as instance as D:\
# will be used as landform fetch cache
$instanceId = (invoke-webrequest http://169.254.169.254/latest/meta-data/instance-id -UseBasicParsing).content
aws --region us-gov-west-1 ec2 attach-volume --volume-id vol-022b586b5117e7521 --instance-id $instanceId --device xvdf
