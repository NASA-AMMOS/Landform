set ver=1.7.0
set queue=m20-ids-g-sqs-landform-lftest1 
set failqueue=m20-ids-g-sqs-landform-lftest1-fail
set dir=c:\landform\Landform-%ver%
set logdir=c:\log\landform-tactical
set tmpdir=c:\temp\landform-tactical

%dir%\Landform.exe process-tactical --service --mission M2020 --queuename %queue% --failqueuename %failqueue% --awsprofile none --awsregion none --meshformat obj --logdir %logdir% --tempdir %tmpdir% --stacktraces
