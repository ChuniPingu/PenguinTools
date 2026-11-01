# Fan charting workflow

PenguinTools helps bring fan charts authored in Margrete to CHUNITHM while preserving their intended playback effects. This workflow provides context for contributors and coding agents working on conversion behavior.

## Tools and formats

| Tool or format | Role |
| --- | --- |
| [Margrete](https://umgr.inonote.jp/margrete/intro/) | Chart editor used to create and revise charts. |
| MGXC | Margrete's editable chart file and the source used for everyday PenguinTools conversion. |
| [UMIGURI](https://umgr.inonote.jp/en/docs/intro/) | CHUNITHM simulator used to watch and play fan charts. |
| UGC | Chart exported by Margrete for playback in UMIGURI; also commonly distributed by chart authors. |
| C2S | Chart format used by CHUNITHM, produced alongside supporting assets when PenguinTools builds an option folder. |

## Authoring and playback

1. Create or edit the chart in Margrete and save the MGXC.
2. Export UGC from Margrete and watch it in UMIGURI using autoplay. Revise the chart as needed.
3. To bring the chart into CHUNITHM, use PenguinTools' option conversion workflow (`option build`). It reads MGXC directly and exports an option folder containing the converted charts and supporting assets.
4. Place that folder in the game to load, then check playback in CHUNITHM.

Autoplay review means watching the chart through and visually judging whether the authored content produces the intended effect. What matters depends on what the author put into that particular chart. Once the result looks close enough, the author plays it to check the experience.

PenguinTools' option naming rules are described in the [option build contract](architecture.md#option-scanning).

## Sharing charts

The usual release includes MGXC, UGC, audio, and the jacket image. A converted option folder is offered as an additional package. UGC input support also serves users working with charts distributed by other authors.

## Evaluating conversion changes

Foahh's personal goal is to preserve the intended playback effects of their own charts when converting them from the Margrete and UMIGURI workflow to CHUNITHM.

Round-trip preservation is an additional capability. Conversion checks should also cover charts authored directly in Margrete, with no saved C2S source or round-trip metadata. Restoring saved records and comparing parsed data provide useful evidence, while autoplay and actual play establish how the result appears and feels.