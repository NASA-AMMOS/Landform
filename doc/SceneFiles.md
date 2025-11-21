<div style="width: 42em"> 

### Example:

```json
{
  version: 1.0
  tilesets: [
    {
      id: "NLF_0000F0606538784_415RASLN0010000000309914_0N00LLJ00",
      image_ids: ["NLF_0000F0606538784_415RASLN0010000000309914_0N00LLJ00", "NRF_0000F0606538784_415RASLN0010000000309914_0N00LLJ00"],
      uri: "https://path/tactical/NLF_0000F0606538784_415RASLN0010000000309914_0N00LLJ00/NLF_0000F0606538784_415RASLN0010000000309914_0N00LLJ00_tileset.json",
      frame_id: "frame_a",
      sols: [ 0 ],
      groups: ["Navcam", "Workspace", "NLF_2481_RASLN_001_0000"],
      show: true,
      options: { ... },
      metadata: {}
    },
    {
      id: "NLF_0000F0606546568_303RASLN0010216000309914_0N00LLJ00",
      image_ids: ["NLF_0000F0606546568_303RASLN0010216000309914_0N00LLJ00", "NRF_0000F0606546568_303RASLN0010216000309914_0N00LLJ00"]
      uri: "https://path/tactical/NLF_0000F0606546568_303RASLN0010216000309914_0N00LLJ00/NLF_0000F0606546568_303RASLN0010216000309914_0N00LLJ00_tileset.json",
      frame_id: "frame_b",
      sols: [ 0 ],
      groups: ["Navcam"],
      show: true,
      metadata: {}
    },
    {
      id: "00000_0010216",
      image_ids: ["NLF_0000F0606546568_303RASLN0010216000309914_0N00LLJ00", "NRF_0000F0606546568_303RASLN0010216000309914_0N00LLJ00", "NLF_0000F0606546568_303RASLN0010216000309914_0N00LLJ00", "NRF_0000F0606546568_303RASLN0010216000309914_0N00LLJ00"]
      uri: "https://path/contextual/0010000/00000_0010216_tileset.json",
      frame_id: "frame_c",
      sols: [ 0 ],
      groups: ["Context"],
      show: false,
      metadata: {}
    }
  ],
  images: [
    {
      id: "NLF_0000F0606538784_415RASLN0010000000309914_0N00LLJ00",
      product_id: "NLF_0000F0606538784_415RASLN0010000000309914_0N00LLJ00",
      uri: "https://path/NLF_0000F0606538784_415RASLN0010000000309914_0N00LLJ00.png",
      thumbnail: "https://path/image/NLF_0000F0606538784_415RASLN0010000000309914_0N00LLJ00_thumb.png",
      frame_id: "frame_c",
      width: 1024,
      height: 1125,
      bands: 3,
      index: 0,
      backprojected_pixels: 0,
      model: {
        type: "CAHV",
        C: [0.8299329,0.4326697,-1.975102],
        A: [0.588082,-2.756146E-05,0.8087914],
        H: [491.3249,4643.612,470.2884],
        V: [-3401.852,119.8225,3217.018],
      },
      metadata: { ... }
    },
    ...
  ],
  frames: [
    {
      id: "frame_a",
      translation: { "x": 0, "y": 0, "z": 0 },
      rotation: { "x": 0, "y": 0, "z": 0, "w": 1 },
      scale: { "x": 1, "y": 1, "z": 1 }
    },
    {
      id: "frame_b",
      translation: { "x": 4, "y": 3, "z": 2 },
      rotation: { "x": 0.8, "y": 1.3, "z": 2, "w": 1 },
      scale: { "x": 1, "y": 1, "z": 1 },
      parent_id: "frame_a"
    },
    {
      id: "frame_c",
      translation: { "x": 2, "y": 0, "z": 4 },
      rotation: { "x": 0.2, "y": 1, "z": 3, "w": 1 },
      scale: { "x": 1, "y": 1, "z": 1 },
      parent_id: "frame_b"
    }
  ]
}
```

### Description:
- Version - file format version, should always be 1.0 for the initial version
- Tilesets - defines a list of tiled mesh products that can be loaded
  - id: a unique name for each tileset
  - image_ids: unique ids of images in this scene that correspond to this tileset.  Typically this means at least that the image camera frustum intersects the tileset geometry, and more specifically, that the image was used to generate at least part of the texture of the tileset.
  - uri: a relative or absolute link to a tileset json file for this mesh; if absent or empty then the tileset json file is implied (e.g. *id*_tileset.json in the same directory as the scene manifest, where *id* is the tileset id)
  - frame_id: the frame this product is associated with.  Note that this is an arbitrary frame definition and does not need to match any specific rover mission frame.  This mesh object will be transformed by this frame (and is parents) when loaded.
  - sols: array of sol numbers from which data for this tileset were sourced
  - groups: a list of unique (at least within this scene) group identifiers that collect related products.  For example, all navcam meshes could be given the tag "navcam".  Then, client apis can support toggling on and off groups of products as well as individually.  "unified meshes" for MSL and M2020, which are lists of other tactical meshes, could be converted to groups.
  - show: specifies if this product is toggled-on (rendered) by default during initial load
  - options: optional overrides for tileset-specific options, see [Unity3DTilesetOptions.cs](https://github.com/NASA-AMMOS/Unity3DTiles/blob/master/Assets/Unity3DTiles/Unity3DTilesetOptions.cs)
  - metadata: a dictionary of additional application specific metadata.  For ASTTRO this can contain specific information such as site, drive, and mission specific frame information
- Images - an array of images available in this scene
  - id: a unique id for the image
  - product_id: mission specfic unique identifier for the image RDR
  - uri: relative or absolute url to a jpg or png version of the image (if absent use *product_id* to look up the image)
  - thumbnail: an optional, smaller version of the image in jpg or png form, defaults to uri if not specified
  - frame_id: the frame this image is associated with.  The image's camera model will be transformed by this frame (and its parents) when loaded.
  - width: image width
  - height: image height
  - bands: number of color bands in the image (1 or 3)
  - index: non-negative integer index of the image for texturing
  - backprojected_pixels: number of context mesh texture pixels that used this image
  - model: the camera model associated with the image
    - type: a named camera model type such as CAHV, CAHVOR, CAHVORE, Pinhole, ect.  This value determines the rest of the named elements in this structure
  - metadata: a dictionary of application specific data.  For ASTTRO this can contain information such as "Stereo_Left", "Mono", "Linearized", site, drive, spacecraft clock, tc
- Frames - a list of frames (i.e. transforms) to apply to products in order to get them into a common reference
  - id: a unique identifier
  - translation: a translation between this frame and its parent.  Defaults to (0, 0, 0).
  - rotation: a quaternion specifying the rotation between this frame.  Defaults to identity.
  - scale: a per-axis scale.  Defaults to (1, 1, 1).
  - parent_id: specifies the id of the parent frame.  This transformation specified by a frame is defined relative to its parent.  If the parent is not specified or is null or empty then the frame is specified to be at the root level.  This must be specified in a tree structure and loops/circular references are invalid.







