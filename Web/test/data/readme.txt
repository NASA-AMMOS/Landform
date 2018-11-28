this subtree contains the metadata but not the binary data for automated test cases

full test data are on s3 under /landlords-dev/landformweb-test-data

these can be run from the Landform/Web dir by commands like

npm install
node tools/runTests.js PATH

where PATH is a path to a full download of the test data tree (or e.g. an s3fs-fuse mount of it)

the server to use and the set of tests to run are defined in landform-test-config.json

each test dir contains the input data and a landform-project-config.json

timestamped output logs from the tests are written to the test dirs


