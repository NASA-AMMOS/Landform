#############################################
# am I running in 32 bit shell?
#############################################
if ($pshome -like "*syswow64*") {
 
  Write-Warning "Restarting script under 64 bit powershell"
 
  # relaunch this script under 64 bit shell
  & (join-path ($pshome -replace "syswow64", "sysnative") powershell.exe) -file `
    (join-path $psscriptroot $myinvocation.mycommand) @args
 
  # exit 32 bit script
  exit
}

$ErrorActionPreference = ‘Stop’ #fail on all errors 

#wait a sec 
Start-Sleep -s 10

#make sure that landform is running 
if((get-process "landform") -eq $Null){ 
        #echo "geez" > c:\\Users\\Administrator\\Desktop\\ohgosh.txt
        throw "landform not running" 
        echo "still here"
        #exit 1&
}

