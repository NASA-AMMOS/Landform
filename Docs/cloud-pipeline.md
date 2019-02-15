# Running Landform Alignment Cloud Pipeline
1.  Create a .txt or .json file containing an array of paths to input .IMG files, typically OPGS navcam RDRs.
   1.  For example, a text file `align-test-inputs.txt` containing the lines
       ```
       s3://red-product/proj/msl/redops/ods/surface/sol/00588/opgs/rdr/ncam
       s3://red-product/proj/msl/redops/ods/surface/sol/00589/opgs/rdr/ncam
       s3://red-product/proj/msl/redops/ods/surface/sol/00590/opgs/rdr/ncam
       ```
       Paths ending in `/**` will be searched recursively.
2.  Create an s3 bucket that will hold the data products, e.g. `s3://landlords-dev/landform-USERNAME`.  Upload your inputs file there.
3.  Build Landform in visual studio in release mode.
4.  `cd Landform/bin/Release`
5.  `Landform.exe configure-cloud --nouserdata`
    * Venue name: accept default or specifiy something different - this is used to prefix DyanmoDB table and SQS queue names.
    * S3 url: use the s3 bucket you created above, e.g. `s3://landlords-dev/landform-USERNAME`
    * AWS region: `us-west-1`
    * AWS profile: `landlords`
    * MSLICE AWS profile: `mslice`
    * MSLICE S3 url: `s3://red-product`
6. `Landform.exe start-align-master PROJ [--inputpath=INPUT] [--debugoutputfolder=DBG] [--startworker] [--verbose] [--redoproject] [--redoobservations] [--redopriors] [--redomasks] [--redofeatures] [--redooverlaps] [--redomatches] [--skipmatching] [--skipbundleadjust] [--matchwithinsitedrives={true|false}] [--adjustwithinsitedrives={true|false}] [--adjustacrosssitedrives={true|false}]` where
   * PROJ is a project name, e.g. `align-test`
   * INPUT is the path to your inputs file, e.g. `s3://landlords-dev/landform-USERNAME/align-test-inputs.txt`.  It can also be a literal path, e.g. `s3://red-product/proj/msl/redops/ods/surface/sol/00589/opgs/rdr/ncam`.  Paths ending in `/**` will be searched recursively.  Input path is required when (re-)creating a project, but is optional if the project already exists (in that case it must match what the project was created with).
   * DBG is a local disk path to a folder for debug outputs
   * adjust within site drives (adjust individual images) defaults to false
   * adjust across site drives defaults to true
