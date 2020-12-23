This subproject compiles to Landform.exe and contains the subcommand drivers comprising the main [Landform "pipeline" batch workflow](https://github.jpl.nasa.gov/OnSight/Landform/wiki/Command-Workflow#landformexe).

Subcommands here generally use either a local or cloud PipelineCore instance to provide database, storage, and logging.  They are designed to be run one after another on a given project ("batch workflow").  Each runs on a single host but may make use of multiple core parallelism (and that host may be an EC2 instance).  

Also see ../TilingServer.
