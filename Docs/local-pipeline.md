# Running Landform Alignment Pipeline Locally

## TLDR Example Workflow
```
./Pipeline/Rover/fetch-msl.sh c:/Users/$USERNAME/Downloads locations basemap 00588 00589 00590
./Landform/bin/Release/Landform.exe configure-local --venue=local --storagedir=c:/Users/$USERNAME/Documents/landform-storage --maxcores=0 --randomseed=-1
./Landform/bin/Release/Landform.exe local-ingest sols588to590 --inputpath=c:/Users/$USERNAME/Downloads/msl/**
./Landform/bin/Release/Landform.exe local-features sols588to590 --writefeatureimages
./Landform/bin/Release/Landform.exe local-matching sols588to590 --writematchimages --writematchmeshes
./Landform/bin/Release/Landform.exe local-bundle-adjust sols588to590 --writedebug
./Landform/bin/Release/Landform.exe local-observation-products sols588to590 --writeallthethings --outputframe=root --usepriors
./Landform/bin/Release/Landform.exe local-observation-products sols588to590 --writeallthethings --outputframe=root
```

Download sols 588 - 590 but process sol 589 only:
```
./Pipeline/Rover/fetch-msl.sh c:/Users/$USERNAME/Downloads locations basemap 00588 00589 00590
./Landform/bin/Release/Landform.exe configure-local --venue=local --storagedir=c:/Users/$USERNAME/Documents/landform-storage --maxcores=0 --randomseed=-1
./Landform/bin/Release/Landform.exe local-ingest sol589 --inputpath=c:/Users/$USERNAME/Downloads/msl/redops/ods/surface/sol/00589/** --locationsxml=c:/Users/$USERNAME/Downloads/msl/locations.xml --basemapdem=c:/Users/$USERNAME/Downloads/msl/out_deltaradii_smg_1m.tif
./Landform/bin/Release/Landform.exe local-features sol589 --writefeatureimages
./Landform/bin/Release/Landform.exe local-matching sol589 --writematchimages --writematchmeshes
./Landform/bin/Release/Landform.exe local-bundle-adjust sol589 --writedebug
./Landform/bin/Release/Landform.exe local-observation-products sol589 --writeallthethings --outputframe=root --usepriors
./Landform/bin/Release/Landform.exe local-observation-products sol589 --writeallthethings --outputframe=root
```

The default is to only detect feature matches and bundle adjust across site drives.  

The sol 588 - 590 dataset has 3688 navcam IMG files, about 5GB total, 263 which we consider observations, 90 images that we actually use for reconstruction.  Total time is currently about 15min.
*   `fetch-msl.sh`: ~3min
*   `local-ingest`: ~3sec
    ```
    processed 3688 files (2.138s), 263 accepted, 0 existing, 0 failed, 3425 skipped
    sitedrive 0003001070: 4 Image observations, 4 Normals observations, 4 Points observations
    sitedrive 0003001208: 4 Image observations, 4 Normals observations, 4 Points observations
    sitedrive 0003001254: 34 Image observations, 34 Normals observations, 34 Points observations
    sitedrive 0003001338: 6 Image observations, 6 Normals observations, 6 Points observations
    sitedrive 0003001366: 1 Image observations
    sitedrive 0003100000: 42 Image observations, 38 Normals observations, 38 Points observations
    total 91 Image observations (90 for reconstruction)
    total 86 Normals observations
    total 86 Points observations
    ```
*   `local-features`: 9min
    ```
    1 images with 400 to 449 features
    1 images with 550 to 599 features
    1 images with 1400 to 1449 features
    1 images with 1800 to 1849 features
    2 images with 2250 to 2299 features
    1 images with 2500 to 2549 features
    1 images with 3700 to 3749 features
    1 images with 3950 to 3999 features
    1 images with 4350 to 4399 features
    1 images with 4750 to 4799 features
    2 images with 4850 to 4899 features
    1 images with 5100 to 5149 features
    3 images with 5250 to 5299 features
    2 images with 5700 to 5749 features
    1 images with 8050 to 8099 features
    1 images with 8650 to 8699 features
    1 images with 9400 to 9449 features
    1 images with 9950 to 9999 features
    64 images with 10000 to 10049 features
    processed 87 reconstruction images (532.296s), computed features for 87 images, 86 with range, 86 existing
    total 746571 features, 575637 with range
    ```
*   `local-matching`: ~4min (cross-site only)
    ```
    4 correspondences with 20 to 29 matches
    1 correspondences with 30 to 39 matches
    3 correspondences with 40 to 49 matches
    3 correspondences with 70 to 79 matches
    5 correspondences with 80 to 89 matches
    2 correspondences with 90 to 99 matches
    2 correspondences with 100 to 109 matches
    2 correspondences with 110 to 119 matches
    1 correspondences with 120 to 129 matches
    1 correspondences with 130 to 139 matches
    1 correspondences with 140 to 149 matches
    rejected 941 image pairs because (step 1) KnownGeometryFilter returned too few matches
    rejected 24 image pairs because (step 2) MoisanStivalFilter returned too few matches
    processed 990 image pairs (239.224s), computed 25 correspondences (0 existing), saved 990
    ```
*   `local-bundle-adjust`: ~20s (cross-site only)
    ```
    adjusting across site drives: True
    adjusting within site drives: False
    adjusting 6 nodes, 6 site drive frames, 0 observation frames
    running bundle adjuster, 87 total images, 2 rounds
    Setting up bundle adjust of 6 frames
    processed 21 correspondences
    processed 2530 tracks
    running Ceres round 0
    ...
    got ceres result after 2.35366702079773s
    Identified 166 bad points on 19 tracks
    Completed trimming bad points from tracks
    running Ceres round 1
    ...
    got ceres result after 0.502443313598633s
    Identified 122 bad points on 55 tracks
    Completed trimming bad points from tracks
    bundle adjust complete (2.926s)
    writing bundle adjust debug point cloud to c:/Users/vona/Documents/landform-storage/local/alignment/AdjustProducts/sols588to590/bundlecloud.ply
    saving transform 1 of 6 adjusted frames
    saving transform 2 of 6 adjusted frames
    saving transform 3 of 6 adjusted frames
    saving transform 4 of 6 adjusted frames
    saving transform 5 of 6 adjusted frames
    saving transform 6 of 6 adjusted frames
    ```
*   `local-observation-products --usepriors`: 17s
    ```
    generated meshes for 86 observations (16.734s)
    ```
*   `local-observation-products`: 17s
    ```
    generated meshes for 86 observations (17.041s)
    ```

## Run Agisoft instead of our bundler
install Agisoft Metashape professional (standard will not work as it doesn't allow python scripting)
./Pipeline/Rover/fetch-msl.sh c:/Users/$USERNAME/Downloads locations 00588 00589 00590
./Landform/bin/Release/Landform.exe configure-local --venue=local --storagedir=c:/Users/$USERNAME/Documents/landform-storage --maxcores=0
./Landform/bin/Release/Landform.exe local-ingest sols588to590 --inputpath=c:/Users/$USERNAME/Downloads/msl/**

the results will be published back to your local database with the agisoft transform source. they can be visualized with observation products by
./Landform/bin/Release/Landform.exe local-observation-products sol589 --adjustedtransformsources=agisoft

## Run Locally but Operate on Cloud Data
All of the local commands (`local-ingest`, `local-features`, `local-matching`, `local-bundle-adjust`, `local-observation-products`) also support a `--cloud` option.  If present, that means that the computation and flow control will be performed locally, but that data will be read from and written to the cloud (i.e. S3 and DynamoDB).  Debug outputs will still be written locally.

Example of full workflow to operate on cloud data:
```
./Landform/bin/Release/Landform.exe configure-local --venue=local --storagedir=c:/Users/$USERNAME/Documents/landform-storage --maxcores=0 --randomseed=-1
./Landform/bin/Release/Landform.exe configure-cloud --venue=landform-dev-$USERNAME-$HOSTNAME --s3url=s3://landlords-dev/landform-$USERNAME --awsregion=us-west-1 --awsprofile=landlords --msliceawsprofile=mslice --mslices3url=s3://red-product --maxcores=0 --randomseed=-1 --nouserdata
./Landform/bin/Release/Landform.exe local-ingest sol589 --cloud --inputpath=s3://red-product/proj/msl/redops/ods/surface/sol/00589/opgs/rdr/ncam/**
./Landform/bin/Release/Landform.exe local-features sol589 --cloud --writefeatureimages
./Landform/bin/Release/Landform.exe local-matching sol589 --cloud --writematchimages --writematchmeshes
./Landform/bin/Release/Landform.exe local-bundle-adjust sol589 --cloud --writedebug
./Landform/bin/Release/Landform.exe local-observation-products sol589 --cloud --writeallthethings --outputframe=root --usepriors
./Landform/bin/Release/Landform.exe local-observation-products sol589 --cloud --writeallthethings --outputframe=root
```

It is also possible to **post-mortem collect stats and generate debug outputs from already-run cloud data**:
```
./Landform/bin/Release/Landform.exe configure-local --venue=local --storagedir=c:/Users/$USERNAME/Documents/landform-storage --maxcores=0 --randomseed=-1
./Landform/bin/Release/Landform.exe configure-cloud --venue=landform-dev-$USERNAME-$HOSTNAME --s3url=s3://landlords-dev/landform-$USERNAME --awsregion=us-west-1 --awsprofile=landlords --msliceawsprofile=mslice --mslices3url=s3://red-product --maxcores=0 --randomseed=-1 --nouserdata
./Landform/bin/Release/Landform.exe local-ingest sol589 --cloud
./Landform/bin/Release/Landform.exe local-features sol589 --cloud --writefeatureimages --tallyexisting
./Landform/bin/Release/Landform.exe local-matching sol589 --cloud --writematchimages --writematchmeshes --tallyexisting
# local-bundle-adjust currently does not have an option to only generate debug outputs
./Landform/bin/Release/Landform.exe local-observation-products sol589 --cloud --writeallthethings --outputframe=root --usepriors
./Landform/bin/Release/Landform.exe local-observation-products sol589 --cloud --writeallthethings --outputframe=root
```

## Long Form
1.  Get some input data.  You will want one or more directories with .IMG files, typically OPGS navcam RDRs.
    1.  To use MSL data from `s3://red-product` you will need the `mslice` AWS credentials in your `~/.aws/credentials` file.
    1.  You can download the data with any S3 tool such as CloudBerry or WinSCP, or use the `Pipeline/Rover/fetch-msl.sh` script.
    1.  A full path would be `s3://red-product/proj/msl/redops/ods/surface/sol/NUM/opgs/rdr/ncam` where NUM is a sol number.
    1.  If you want to use MSLLocations priors you will also need
        1.  the MSL locations XML file from `http://mars.jpl.nasa.gov/msl-raw-images/locations.xml` (no authentication required)
        1.  the MSL basemap DEM from `s3://12landlords/TerrainSourceAssets/basemaps/out_deltaradii_smg_1m.tif`
    1.  To use `fetch-msl.sh` you will need a sh-compatible command prompt (e.g. git bash, cygwin, or WSL). You will also need the AWS command line interface (install latest python3, `pip install awscli --upgrade --user`,  your PATH environment variable must include `%USERPROFILE%\AppData\Roaming\Python\Python??\Scripts`).
    1.  Some example sol numbers are `00588`, `00589`, `00590`.  So you could run e.g. `./Pipeline/Rover/fetch-msl.sh DIR locations basemap 00588 00589 00590` where DIR is where you'd like to download the input data (an `msl` subdir will be created), e.g. `c:/Users/USER/Downloads`.  The script fetches the OPGS navcam RDR .IMG products (only) for the specified sols.  It also fetches
        *   locations.xml if "locations" is included in the argument list
        *   the basemap DEM if "basemap" is included in the arguments list (for this you will need a "landlords" profile in your ~/.aws/credentials)
        *   the MSS processed Mastcams if "mastcams" is included in the arguments list
1.  Build Landform in visual studio in Release mode.
1.  **`./Landform/bin/Release/Landform.exe configure-local`** accept the default `local` as venue name, and specify an absolute path (a relative path should work too but may get confusing) for the storage dir, e.g. `c:/Users/USER/Documents/landform-storage`.  The directory does not need to exist yet.
1.  **`./Landform/bin/Release/Landform.exe local-ingest PROJ`** where PROJ is a project name, e.g. `msl`.  Options include
    * `--inputpath=INPUT` required when (re-)creating a project, but optional if the project already exists; in that case if it's specified it must match what the project was created with.  Either a directory or a .txt or .json file containing an array of directories.  Directories ending in `/**` will be searched recursively.  For example, if you downloaded some data with `fetch-msl.sh` and you want to use all of it, you could specify `DIR/msl/**` as INPUT where DIR is the same directory you specified to `fetch-msl.sh`.
    * `--onlyforsitedrives=SSSSSDDDDD[,SSSSSDDDDD[,...]]`
    * `--redoproject`, `--redoobservations`, `--redopriors`
    * `--quiet`, `--verbose`, `--debug`, `--noprogress`, `--singlethreaded`
1.  **`./Landform/bin/Release/Landform.exe local-features PROJ`**.  Options include
    * `--redofeatures`, `--detectortype=ASIFT`, `--maxfeaturesperimage=1000`, `--minfeaturesize=0`
    * `--norange`
    * `--writefeatureimages`, `--imageformat=png`, `--imageoutputfolder=DIR` default output folder is the project storage under `alignment/FeatureProducts`
    * `--quiet`, `--verbose`, `--debug`, `--noprogress`, `--singlethreaded`
1.  **`./Landform/bin/Release/Landform.exe local-matching PROJ`**.  Options include
    * `--redooverlaps`, `--redomatches`, `--matchwithinsitedrives`, `--minmatchesperpair=20`
    * `--writematchimages`, `--writematchmeshes`, `--outputfolder=DIR` default output folder is project storage under `alignment/FeatureProducts`
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


