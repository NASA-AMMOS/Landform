# Running Landform Alignment Pipeline Locally

1.  Get some input data.  You will want one or more directories with .IMG files, typically OPGS navcam RDRs.
    1.  To use MSL data from `s3://red-product` you will need the `mslice` AWS credentials in your `~/.aws/credentials` file.
    1.  You can download the data with any S3 tool such as CloudBerry or WinSCP, or use the `Pipeline/Rover/fetch-msl.sh` script.
    1.  A full path would be `s3://red-product/proj/msl/redops/ods/surface/sol/NUM/opgs/rdr/ncam` where NUM is a sol number.
    1.  You will also need the MSL locations XML file from `http://mars.jpl.nasa.gov/msl-raw-images/locations.xml` (no authentication required).
    1.  To use `fetch-msl.sh` you will need a sh-compatible command prompt (e.g. git bash, cygwin, or WSL). You will also need the AWS command line interface (install latest python3, `pip install awscli --upgrade --user`,  your PATH environment variable must include `%USERPROFILE%\AppData\Roaming\Python\Python??\Scripts`).
    1.  Some example sol numbers are `00588`, `00589`, `00590`.  So you could run e.g. `./Pipeline/Rover/fetch-msl.sh DIR locations 00588 00589 00590` where DIR is where you'd like to download the input data (an `msl` subdir will be created), e.g. `c:/Users/USER/Downloads`.  The script fetches the OPGS navcam RDR .IMG products (only) for the specified sols.  It also fetches locations.xml if "locations" is included in the argument list.
1.  Build Landform in visual studio in Release mode.
1.  `./Landform/bin/Release/Landform.exe configure-local` accept the default `local` as venue name, and specify an absolute path (a relative path should work too but may get confusing) for the storage dir, e.g. `c:/Users/USER/Documents/landform-storage`.  The directory does not need to exist yet.
1.  `./Landform/bin/Release/Landform.exe local-ingest PROJ [--inputpath=INPUT] [--verbose] [--redoproject] [--redoobservations] [--redopriors]` where PROJ is a project name (e.g. `msl`) and INPUT is either a directory or a .txt or .json file containing an array of directories.  Directories ending in `/**` will be searched recursively.  For example, if you downloaded some data with `fetch-msl.sh` and you want to use all of it, you could specify `DIR/msl/**` as INPUT where DIR is the same directory you specified to `fetch-msl.sh`.  Input path is required when (re-)creating a project, but is optional if the project already exists (in that case it must match what the project was created with).
1.  `./Landform/bin/Release/Landform.exe local-masks PROJ [--verbose] [--redomasks]`
1.  `./Landform/bin/Release/Landform.exe local-features PROJ [--verbose] [--redofeatures] [--detectortype=ASIFT]`
1.  `./Landform/bin/Release/Landform.exe local-matching PROJ [--verbose] [--redooverlaps] [--redomatches] [--matchwithinsitedrives]`
1.  `./Landform/bin/Release/Landform.exe local-bundle-adjust PROJ [--verbose] [--adjustwithinsitedrives] [--noadjustacrosssitedrives] [--bundleadjustrounds=2] [--debugoutputfolder=DBG]`

On my 36 core machine this workflow takes about about 15 minutes for cross-site adjustment of sols 588, 589, 590, or 43 min for adjusting all images (but we can probably get that one down by a lot as I think the bulk of the time is being spent in a Ceres bundle solve where the convergence criteria is too tight, see https://github.jpl.nasa.gov/OnSight/Landform/issues/414).  This dataset has 3688 navcam IMG files, about 5GB total, 263 which we consider observations, 90 images that we actually use for reconstruction.
* download: 3min using fetch-msl.sh
* ingest: 2.4sec
* masks: 7.6sec
* features: 9min
* matching: 2.5min cross-site only (958 candidate image pairs, 17 keepers), 5min all (1662 candidates, 381 keepers)
* bundle adjust: 2.3s site-drive frames only (6 adjusted nodes), 28.6min all frames (93 adjusted nodes) 
