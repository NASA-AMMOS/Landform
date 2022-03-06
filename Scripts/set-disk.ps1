# mount NVMe ephemeral volume instance as E:\
# will be used as landform storage and temp
Set-Disk 1 -isOffline $false
Initialize-Disk 1 -PartitionStyle GPT
New-Partition -DiskNumber 1 -UseMaximumSize -DriveLetter E
Format-Volume -DriveLetter E -FileSystem NTFS -NewFileSystemLabel landform-storage
