# Running Landform Alignment Cloud Pipeline
1.  Build Landform in visual studio in release mode.
1.  `./Landform/bin/Release/Landform.exe configure-cloud --nouserdata`
    * Venue name: accept default or specifiy something different - this is used to prefix DyanmoDB tables, S3 paths, and SQS queue names.
    * S3 url: `s3://landlords-dev`
    * AWS region: `us-west-1`
    * AWS profile: `landlords`
    * MSLICE AWS profile: `mslice`
    * MSLICE S3 url: `s3://red-product`
1.  Create a .txt or .json file containing an array of paths to input .IMG files, typically OPGS navcam RDRs.
   1.  For example, a text file `align-test-inputs.txt` containing the lines
       ```
       s3://red-product/proj/msl/redops/ods/surface/sol/00588/opgs/rdr/ncam
       s3://red-product/proj/msl/redops/ods/surface/sol/00589/opgs/rdr/ncam
       s3://red-product/proj/msl/redops/ods/surface/sol/00590/opgs/rdr/ncam
       ```
       Paths ending in `/**` will be searched recursively.
1.  Create an s3 bucket that will hold the data products `s3://landlords-dev/VENUE`.  Upload your inputs file there.
1. `./Landform/bin/Relese/Landform.exe start-align-master PROJ [--inputpath=INPUT] [--onlyforsitedrives=SSSSSDDDDD[,SSSSSDDDDD[,...]]] [--startworker] [--verbose] [--redoproject] [--redoobservations] [--redopriors] [--redomasks] [--redofeatures] [--redooverlaps] [--redomatches] [--skipmatching] [--skipbundleadjust] [--matchwithinsitedrives] [--adjustwithinsitedrives] [--noadjustacrosssitedrives] [--bundleadjustrounds=2] [--bundleadjustdebugoutputfolder=DBG]` where
   * PROJ is a project name, e.g. `align-test`
   * INPUT is the path to your inputs file, e.g. `s3://landlords-dev/VENUE/align-test-inputs.txt`.  It can also be a literal path, e.g. `s3://red-product/proj/msl/redops/ods/surface/sol/00589/opgs/rdr/ncam`.  Paths ending in `/**` will be searched recursively.  Input path is required when (re-)creating a project, but is optional if the project already exists (in that case it must match what the project was created with).
   * DBG is a local disk path to a folder for debug outputs

On my 36 core machine this workflow takes about about 18 minutes for cross-site adjustment of sols 588, 589, 590.  This dataset has 3688 navcam IMG files, about 5GB total, 263 which we consider observations, 90 images that we actually use for reconstruction.
* ingest: 1.5min
* masks and features: 9min
* matching: 6min
* bundle adjust: 2.3s site-drive frames only (6 adjusted nodes)
