# Change Log

| Revision   | Submission Date (dd-mm-yyy) | Affected Sections or Pages | Change Summary                              |
|------------|-----------------------------|----------------------------|---------------------------------------------|
| Initial    | 23-10-2019                  | All                        | Initial issue of document.                  |


# Document Overview

## Identification

| Property       | Value                                                                                              |
|----------------|----------------------------------------------------------------------------------------------------|
| Element        | IDS                                                                                                |
| Program Set    | Geometry Streaming Server                                                                          |
| Version        | 1.5.2                                                                                              |

## Purpose

The Geometry Streaming Server securely converts textured triangle meshes to tilesets in the 3DTiles format:

<https://github.com/AnalyticalGraphicsInc/3d-tiles>

Potentially large meshes are broken up into a hierarchy of tiles that can be scalably streamed to clients.

This software supports both generating 3DTiles tilesets and serving them to clients, and so is also referred to as both a "Mesh Tiling Server" and a "Geometry Streaming Server".

Included in this manual are:

* instructions for deploying to Amazon Web Services
* a manual for the REST API, which is the interface for uploading input meshes and converting them to 3DTiles tilesets
* test instructions.

## Overview

The Geometry Streaming Server (GSS) is an assembly under the Instrument Data System (IDS) subsystem of MGSS which provides tools and services in the area of instrument data processing.  The purpose of GSS is to support the distribution and display of large 3D datasets. Specifically, GSS is a web service that accepts triangulated meshes as input and reprocesses them into a set of tiles optimized for real-time streaming.  This service enables frontend display tools to interactively download and display only the portion a mesh required to fill a user's current field of view, allowing large meshes to be displayed efficiently.

## Terminology and Notation

This document uses standard terminolgy and notation.

Acronyms and Abberviations:

|       |
|-------|-------------------------------------------------------------------------------------------------------------|
| 3D    | Three Dimensional                                                                                           |
| 3DES  | Triple Data Encryption Standard algorithm                                                                   |
| AES   | Advanced Encryption Standard                                                                                |
| AMMOS | Advanced Multi-Mission Operations System                                                                    |
| API   | Application Programming Interface                                                                           |
| ASCII | American Standard Code for Information Interchange                                                          |
| AWS   | Amazon Web Services                                                                                         |
| EC2   | Elastic Compute Cloud                                                                                       |
| FPS   | Frames Per Second                                                                                           |
| GDAL  | Geospatial Data Abstraction Library                                                                         |
| glTF  | GL (as in OpenGL: Open Graphics Library) Transmission Format                                                |
| GSS   | Geometry Streaming Server                                                                                   |
| GUI   | Graphical User Interface                                                                                    |
| HTTP  | Hypertext Transfer Protocol                                                                                 |
| HTTPS | Hypertext Transfer Protocol Secure                                                                          |
| IDS   | Instrument Data Systems                                                                                     |
| JPG   | JPEG: Joint Pictures Experts Group                                                                          |
| JSON  | Javascript Object Notation                                                                                  |
| MGSS  | Multimission Ground Systems and Services                                                                    |
| OBJ   | Geometry definition file format                                                                             |
| PLY   | Polygon file format                                                                                         |
| PNG   | Portable Network Graphics                                                                                   |
| REST  | Representational State Transfer                                                                             |
| S3    | Simple Storage Service                                                                                      |
| SDK   | Software Development Kit                                                                                    |
| SQS   | Simple Queue Service                                                                                        |
| SSO   | Single Sign-On                                                                                              |
| TIFF  | Tagged Image File Format                                                                                    |
| TLS   | Transport Layer Security                                                                                    |
| UV    | "U" and "V" denote the axes of the two dimensional texture                                                  |

## References

Applicable JPL Rules documents:

| Title                                                                                             | DocID           |
|---------------------------------------------------------------------------------------------------|-----------------|
| Software Development, Rev 9                                                                       |        57653    |

Applicable MGSS Documents:

| Title                                                                                             | Document Number |
|---------------------------------------------------------------------------------------------------|-----------------|
| AMMOS Technical Standards Profile, Rev A                                                          | DOC-001101      |
| MGSS Implementation and Maintenance Task Requirements, Rev D                                      | DOC-001455      |
| Geometry Streaming Server Software Requirements Document                                          | DOC-002000      |



