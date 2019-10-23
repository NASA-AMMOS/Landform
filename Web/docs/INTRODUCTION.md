# Introduction

The Landform Mesh Tiling Server securely converts textured triangle meshes to tilesets in the 3DTiles format:

<https://github.com/AnalyticalGraphicsInc/3d-tiles>

Potentially large meshes are broken up into a hierarchy of tiles that can be scalably streamed to clients.

This software also supports serving the generated 3DTiles tilesets to clients, and so is also referred to as a "Geometry Streaming Server".

Included in this manual are:

* instructions for deploying to Amazon Web Services
* a manual for the REST API, which is the interface for uploading input meshes and converting them to 3DTiles tilesets
* test instructions.

