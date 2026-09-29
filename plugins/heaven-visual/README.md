# Heaven Visual

Cheap local visual preprocessing before expensive upstream reasoning.

PNG/GIF/JPEG dimensions and SHA-256 probing work with the Python standard library. Tile planning lets agents split large screenshots/images into bounded regions. Resize/crop use an optional Pillow dependency loaded only when those transforms are requested.

All paths are physically contained beneath an allowed root; transforms require confirmation before overwriting an existing destination.

Validate with `python .\plugins\heaven-visual\verify.py`.
