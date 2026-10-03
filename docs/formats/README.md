# Chart formats

Reference material for the chart parsers and writers in `PenguinTools.Chart/`.

| Document                                               | Contents                                         |
| ------------------------------------------------------ | ------------------------------------------------ |
| [C2S note types](c2s.md)                               | Record fields, note codes, colors, and effects   |
| [SUS](sus.md)                                          | English reference for SUS v2.7 rev2              |
| [UGC](ugc.md)                                          | Umiguri Chart v8 commands and project extensions |
| [Alternate SUS translation](references/sus-waki285.md) | Retained upstream translation and annotations    |

These documents describe file syntax; implementation coverage is determined by the parsers, writers, and tests. Keep format version numbers because they identify the data specification. Tool and library versions belong in configuration.

SUS and UGC references retain their original source attribution. The alternate SUS translation is source material; use the primary SUS document for corrected examples.

For model ownership and conversion behavior, see [architecture](../architecture.md#charts).
