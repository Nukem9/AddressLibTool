A utility for extracting and updating `REL::Offset` and `REL::ID` code patterns. This scans a set of files, reads `finalized.csv` which contains a row for each REL entry, then automatically updates the source code. Paths are hardcoded. No support is provided.

Format of `finalized.csv`:
```
OldRELID, OldRelativeAddress, OldVirtualAddress, GeneratedSignature, NewVirtualAddress
11045, FCFE0, 1400FCFE0, 40 53 48 83 EC 20 83 3D ? ? ? ? ? 74, 140106EC0
```