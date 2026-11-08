# Pure3D chunk payload readers

`P3dChunkDataFactory.cs` retains the explicit chunk-id dispatch switch. Payload
readers are organized into partial files by format family:

| File suffix | Payloads |
| --- | --- |
| `Materials` | Textures, images, shaders, and shader parameters |
| `Particles` | Legacy particle systems and emitters |
| `Animation` | Channels, frame controllers, and animation groups |
| `Geometry` | Billboards, skeletons, meshes, and vertex streams |
| `Scene` | Composite drawables, scenegraph nodes, bounds, and cameras |
| `Physics` | Physics objects, collision volumes, intersections, and terrain |
| `World` | State props, attributes, locators, and export metadata |
| `Common` | Metadata and shared typed-list readers |

Binary read order is part of the format contract. In particular, several legacy
particle and animation chunks read version before name, while mesh/material
chunks read name before version. Keep that order explicit in the dispatch switch.
Unsupported payloads remain unknown without consuming their bytes; truncation
continues to report the reader's existing overrun errors.

`EngineNetTest/P3dChunkDataTests.cs` covers representative field-order, channel,
unknown-chunk, and truncation cases without making parser internals public.
