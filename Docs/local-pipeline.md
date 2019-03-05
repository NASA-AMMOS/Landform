# Running Landform Alignment Pipeline Locally

## TLDR Example Workflow
```
./Pipeline/Rover/fetch-msl.sh c:/Users/$USERNAME/Downloads locations 00588 00589 00590
./Landform/bin/Release/Landform.exe configure-local --venue=local --storagedir=c:/Users/$USERNAME/Documents/landform-storage --maxcores=0
./Landform/bin/Release/Landform.exe local-ingest sols588to590 --inputpath=c:/Users/$USERNAME/Downloads/msl/**
./Landform/bin/Release/Landform.exe local-features sols588to590 --writefeatureimages
./Landform/bin/Release/Landform.exe local-matching sols588to590 --writematchimages
./Landform/bin/Release/Landform.exe local-bundle-adjust sols588to590 --writedebug
./Landform/bin/Release/Landform.exe local-observation-products sols588to590 --writeallthethings --outputframe=root --usepriors
./Landform/bin/Release/Landform.exe local-observation-products sols588to590 --writeallthethings --outputframe=root
```

On my 36 core machine this workflow takes about about 15 minutes for cross-site adjustment of sols 588, 589, 590, or 43 min for adjusting all images (but we can probably get that one down by a lot as I think the bulk of the time is being spent in a Ceres bundle solve where the convergence criteria is too tight, see https://github.jpl.nasa.gov/OnSight/Landform/issues/414).  This dataset has 3688 navcam IMG files, about 5GB total, 263 which we consider observations, 90 images that we actually use for reconstruction.
* download: 3min using fetch-msl.sh
* ingest: 2.4sec
* features: 9min
* matching: 2.5min cross-site only (958 candidate image pairs, 17 keepers), 5min all (1662 candidates, 381 keepers)
* bundle adjust: 2.3s site-drive frames only (6 adjusted nodes), 28.6min all frames (93 adjusted nodes) 

## Run Locally but Operate on Cloud Data
All of the local commands (`local-ingest`, `local-features`, `local-matching`, `local-bundle-adjust`, `local-observation-products`) also support a `--cloud` option.  If present, that means that the computation and flow control will be performed locally, but that data will be read from and written to the cloud (i.e. S3 and DynamoDB).  Debug outputs will still be written locally.

Example of full workflow to operate on cloud data:
```
./Landform/bin/Release/Landform.exe configure-local --venue=local --storagedir=c:/Users/$USERNAME/Documents/landform-storage --maxcores=0
./Landform/bin/Release/Landform.exe configure-cloud --venue=landform-dev-$USERNAME-$HOSTNAME --s3url=s3://landlords-dev/landform-$USERNAME --awsregion=us-west-1 --awsprofile=landlords --msliceawsprofile=mslice --mslices3url=s3://red-product --maxcores=0 --nouserdata
./Landform/bin/Release/Landform.exe local-ingest sol589 --cloud --inputpath=s3://red-product/proj/msl/redops/ods/surface/sol/00589/opgs/rdr/ncam/**
./Landform/bin/Release/Landform.exe local-features sol589 --cloud --writefeatureimages
./Landform/bin/Release/Landform.exe local-matching sol589 --cloud --writematchimages
./Landform/bin/Release/Landform.exe local-bundle-adjust sol589 --cloud --writedebug
./Landform/bin/Release/Landform.exe local-observation-products sol589 --cloud --writeallthethings --outputframe=root --usepriors
./Landform/bin/Release/Landform.exe local-observation-products sol589 --cloud --writeallthethings --outputframe=root
```

It is also possible to **post-mortem collect stats and generate debug outputs from already-run cloud data**:
```
./Landform/bin/Release/Landform.exe configure-local --venue=local --storagedir=c:/Users/$USERNAME/Documents/landform-storage --maxcores=0
./Landform/bin/Release/Landform.exe configure-cloud --venue=landform-dev-$USERNAME-$HOSTNAME --s3url=s3://landlords-dev/landform-$USERNAME --awsregion=us-west-1 --awsprofile=landlords --msliceawsprofile=mslice --mslices3url=s3://red-product --maxcores=0 --nouserdata
./Landform/bin/Release/Landform.exe local-ingest sol589 --cloud
./Landform/bin/Release/Landform.exe local-features sol589 --cloud --writefeatureimages --tallyexisting
./Landform/bin/Release/Landform.exe local-matching sol589 --cloud --writematchimages --tallyexisting
# local-bundle-adjust currently does not have an option to only generate debug outputs
./Landform/bin/Release/Landform.exe local-observation-products sol589 --cloud --writeallthethings --outputframe=root --usepriors
./Landform/bin/Release/Landform.exe local-observation-products sol589 --cloud --writeallthethings --outputframe=root
```

## Long Form
1.  Get some input data.  You will want one or more directories with .IMG files, typically OPGS navcam RDRs.
    1.  To use MSL data from `s3://red-product` you will need the `mslice` AWS credentials in your `~/.aws/credentials` file.
    1.  You can download the data with any S3 tool such as CloudBerry or WinSCP, or use the `Pipeline/Rover/fetch-msl.sh` script.
    1.  A full path would be `s3://red-product/proj/msl/redops/ods/surface/sol/NUM/opgs/rdr/ncam` where NUM is a sol number.
    1.  You will also need the MSL locations XML file from `http://mars.jpl.nasa.gov/msl-raw-images/locations.xml` (no authentication required).
    1.  To use `fetch-msl.sh` you will need a sh-compatible command prompt (e.g. git bash, cygwin, or WSL). You will also need the AWS command line interface (install latest python3, `pip install awscli --upgrade --user`,  your PATH environment variable must include `%USERPROFILE%\AppData\Roaming\Python\Python??\Scripts`).
    1.  Some example sol numbers are `00588`, `00589`, `00590`.  So you could run e.g. `./Pipeline/Rover/fetch-msl.sh DIR locations 00588 00589 00590` where DIR is where you'd like to download the input data (an `msl` subdir will be created), e.g. `c:/Users/USER/Downloads`.  The script fetches the OPGS navcam RDR .IMG products (only) for the specified sols.  It also fetches locations.xml if "locations" is included in the argument list.
1.  Build Landform in visual studio in Release mode.
1.  **`./Landform/bin/Release/Landform.exe configure-local`** accept the default `local` as venue name, and specify an absolute path (a relative path should work too but may get confusing) for the storage dir, e.g. `c:/Users/USER/Documents/landform-storage`.  The directory does not need to exist yet.
1.  **`./Landform/bin/Release/Landform.exe local-ingest PROJ`** where PROJ is a project name, e.g. `msl`.  Options include
    * `--inputpath=INPUT` required when (re-)creating a project, but optional if the project already exists; in that case if it's specified it must match what the project was created with.  Either a directory or a .txt or .json file containing an array of directories.  Directories ending in `/**` will be searched recursively.  For example, if you downloaded some data with `fetch-msl.sh` and you want to use all of it, you could specify `DIR/msl/**` as INPUT where DIR is the same directory you specified to `fetch-msl.sh`.
    * `--onlyforsitedrives=SSSSSDDDDD[,SSSSSDDDDD[,...]]`
    * `--redoproject`, `--redoobservations`, `--redopriors`
    * `--quiet`, `--verbose`, `--debug`, `--noprogress`, `--singlethreaded`
1.  **`./Landform/bin/Release/Landform.exe local-features PROJ`**.  Options include
    * `--redofeatures`, `--detectortype=ASIFT`, `--maxfeaturesperimage=1000`, `--minfeaturesize=0`
    * `--writefeatureimages`, `--imageformat=png`, `--imageoutputfolder=DIR` default output folder is the project storage under `alignment/FeatureProducts`
    * `--quiet`, `--verbose`, `--debug`, `--noprogress`, `--singlethreaded`
1.  **`./Landform/bin/Release/Landform.exe local-matching PROJ`**.  Options include
    * `--redooverlaps`, `--redomatches`, `--matchwithinsitedrives`, `--minmatchesperpair=20`
    * `--writematchimages`, `--imageoutputfolder=DIR` default output folder is project storage under `alignment/FeatureProducts`
    * `--quiet`, `--verbose`, `--debug`, `--noprogress`, `--singlethreaded`
1.  **`./Landform/bin/Release/Landform.exe local-bundle-adjust PROJ`**.  Options include
    * `--adjustwithinsitedrives`, `--noadjustacrosssitedrives`, `--bundleadjustrounds=2`
    * `--writedebug`, `--debugoutputfolder=DBG` default output folder is project storage under `alignment/AdjustProducts`
    * `--quiet`, `--verbose`, `--debug`, `--noprogress`, `--singlethreaded`
1.  **`Landform.exe local-observation-products PROJ`** generates mesh and image products for each observation in the database.  Options include
    * `--onlyforsitedrives=SSSSSDDDDD[,SSSSSDDDDD[,...]]` Only generate meshes for specific site drives, comma separated
    * `--outputfolder` Output directory, or omit to save to project storage under `alignment/ObservationProducts`. By default subdirectories will be created in the pattern FRAME/TYPE/PROJ/SITEDRIVE where FRAME corresponds to the output coordinate frame and TYPE indicates whether adjusted transforms or only priors were used and/or if the transform sources were limited to a subset of those available.
    * `--outputframe` (Default: rover) Output coordinate frame: rover, sitedrive, or root
    * `--allowmastcam`(Default: false) Create meshes for mastcam observations
    * `--requirenormals` (Default: false) Only create meshes for observations with normals
    * `--requiretextures` (Default: false) Only create meshes for observations with textures
    * `--nowedgemeshes` (Default: false) Don't write wedge meshes
    * `--noimages` (Default: false) Don't write observation images (and don't texture wedge meshes)
    * `--meshformat` (Default: ply) Mesh format, e.g. ply, obj 
    * `--imageformat` (Default: jpg) Texture image format, e.g. png, jpg
    * `--pointcloud` (Default: false) Create point clouds instead of triangle meshes
    * `--adjustedtransformsources` Allowed sources for adjusted transforms, comma separated, all if empty (Adjusted,Manual,Landform,Agisoft)
    * `--priortransformsources` Allowed sources for transform priors, comma separated, all if empty (Prior,PlacesDB,LocationsDB,PDS)
    * `--usepriors` (Default: false) Use transform priors only
    * `--decimatemeshes` (Default: 4) Mesh decimation blocksize
    * `--decimateimages` (Default: 2) Texture decimation blocksize
    * `--maxtriangleaspect` (Default: 10) Max triangle aspect ratio
    * `--scalenormalsbyconfidence` (Default: false) Scale normals by confidence
    * `--suppresssitedrivedirectories` (Default: false) Don't split output by site drive
    * `--writefrustumhullmeshes` (Default: false) Write camera frustum hull meshes
    * `--writeuncertaintyinflatedfrustumhullmeshes` (Default: false) Write uncertainty inflated camera frustum hull meshes
    * `--writeallthethings` (Default: false) Write all the things
    * `--noprogress` (Default: false) Hide progress
    * `--verbose` (Default: false) Log verbose info
    * `--debug` (Default: false) Log debug info


