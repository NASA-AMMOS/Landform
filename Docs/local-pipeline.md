# Running Landform Alignment Pipeline Locally

1.  Get some input data.  You will want one or more directories with .IMG files, typically OPGS navcam RDRs.
    1.  To use MSL data from `s3://red-product` you will need the `mslice` AWS credentials in your `~/.aws/credentials` file.
    2.  You can download the data with any S3 tool such as CloudBerry or WinSCP, or use the `Pipeline/Rover/fetch-msl.sh` script.
    3.  Some example sol numbers are `00588`, `00589`, `00590`.
    4.  A full path would be `s3://red-product/proj/msl/redops/ods/surface/sol/NUM/opgs/rdr/ncam` where NUM is a sol number.
    5.  You will also need the MSL locations XML file from `http://mars.jpl.nasa.gov/msl-raw-images/locations.xml` (no authentication required).
    6.  To use `fetch-msl.sh` you will need a sh-compatible command prompt (e.g. git bash, cygwin, or WSL). You will also need the AWS command line interface (install latest python3, `pip install awscli --upgrade --user`,  your PATH environment variable must include `%USERPROFILE%\AppData\Roaming\Python\Python??\Scripts`).
    7.  `./Pipeline/Rover/fetch-msl.sh DIR locations 00588 00589 00590` where DIR is where you'd like to download the input data (an `msl` subdir will be created), e.g. `c:/Users/USER/Downloads`.  The script fetches the OPGS navcam RDR .IMG products (only) for the specified sols.  It also fetches locations.xml if "locations" is included in the argument list.  This example downloads about 5GB.
2.  Build Landform in visual studio in Release mode.
3.  `./Landform/bin/Release/Landform.exe configure-local` accept the default `local` as venue name, and specify an absolute path (a relative path should work too but may get confusing) for the storage dir, e.g. `c:/Users/USER/Documents/landform-storage`.  The directory does not need to exist yet.
4.  `./Landform/bin/Release/Landform.exe local-ingest PROJ [--inputpath=INPUT] [--verbose] [--redoproject] [--redoobservations] [--redopriors]` where PROJ is a project name (e.g. `msl`) and INPUT is either a directory or a .txt or .json file containing an array of directories.  Directories ending in `/**` will be searched recursively.  For example, if you downloaded some data with `fetch-msl.sh` and you want to use all of it, you could specify `DIR/msl/**` as INPUT where DIR is the same directory you specified to `fetch-msl.sh`.  Input path is required when (re-)creating a project, but is optional if the project already exists (in that case it must match what the project was created with).
5.  `./Landform/bin/Release/Landform.exe local-masks PROJ [--verbose] [--redomasks]`
6.  `./Landform/bin/Release/Landform.exe local-features PROJ [--verbose] [--redofeatures] [--detectortype=ASIFT]`
7.  `./Landform/bin/Release/Landform.exe local-matching PROJ [--verbose] [--redooverlaps] [--redomatches] [--matchwithinsitedrives={true|false}]`
8.  `./Landform/bin/Release/Landform.exe local-bundle-adjust PROJ [--verbose] [--adjustwithinsitedrives={true|false}] [--adjustacrosssitedrives={true|false}] [--bundleadjustrounds=2] [--debugoutputfolder=DBG]`

On my 36 core machine this particular workflow takes about TODO minutes for sols 588, 589, 590.  There are 3688 navcam IMG files, 263 of which we recognize as observations, and 91 of those are actual images that we use for reconstruction.
