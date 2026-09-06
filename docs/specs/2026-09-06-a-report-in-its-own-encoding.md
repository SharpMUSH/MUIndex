# A report in its own encoding

`CHARSET-MSSP`: a second operator override, for games whose report is not in the same encoding as
their connect screen.

## The measurement this starts from

Two games in the live catalogue send a connect screen in a legacy encoding and an MSSP report in
UTF-8. Both were probed on 2026-09-06; the bytes below are what came back.

`doom.twmuds.com:4000` — MSSP `NAME`, 39 bytes:

```
44 6f 6f 6d 20 6f 66 20 4c 6f 73 74 20 4b 69 6e 67 64 6f 6d 73 20 28
e5 a4 b1 e8 90 bd e7 9a 84 e5 9c 8b e5 ba a6 29
   as utf-8 : Doom of Lost Kingdoms (失落的國度)
   as big5  : illegal multibyte sequence
```

Its connect screen, same session:

```
5b 48 5b 32 4a 20 ... a2 62 a2 63 a2 64 a2 65 a2 66 a2 67 ...
   as utf-8 : invalid start byte
   as big5  : ▁▂▃▄▅▆▇█     歡  迎  來  到     █▇▆▅▄▃▂▁
```

`115.29.226.27:4000` is the same shape with GBK: an MSSP `NAME` of `e7 82 8e e9 be 99 e5 b0 81 e5
8d b0` (valid UTF-8, 炎龙封印) against a screen of `a1 f9` repeated (GBK ※, and not well-formed
UTF-8 at all).

Neither game is malfunctioning. World text is legacy because the game is; the report is UTF-8
because it is generally a config file somebody wrote in a modern editor. **No single value of
`CHARSET` is correct for both channels on either host.**

## What that costs today

`WireEncoding.Read` takes one override and applies it to the whole session. `Override(overrideName)`
short-circuits ahead of the UTF-8 check, so a staff `CHARSET` decodes the screen *and* the report.

Setting `Big5` on `doom` therefore fixed its screen and turned its name into `Doom of Lost Kingdoms
(憭梯??摨?` — lossily, because Big5 has no round trip for those bytes. The catalogue carried that
for eighteen days. Both games' names had been read correctly before the override existed; the
change feed shows both regressing on 2026-08-19, the day the overrides were set.

The override made the report strictly worse than no override at all. Without one, `doom` fails the
UTF-8 check on its screen and falls to Latin-1 — wrong, but `Undetermined` and reversible, so the
bytes survive for a later repair. With one, they do not.

## What this is not

**Not a detector.** The obvious rule — try strict UTF-8 on the report, fall back to the declared
charset — was measured against the registry before being rejected. Against the twenty real MSSP
values from legacy-charset games it produced no false positives, but that is not the case it has to
survive. `115.29.226.27`'s twelve bytes decode *without error* under both UTF-8 and GBK, yielding
炎龙封印 and 鐐庨緳灏佸嵃. Nothing about the bytes separates them; only knowing that one is words
and the other is noise does. Synthetically, short GBK strings — and game names are two to four
characters — validate as UTF-8 at 3.3% for two characters and 0.60% for three.

A rule that is right most of the time and writes its answer into a public record is rule 5 with
extra steps. `doom` would have been fixed by it only because Big5 happened to hard-fail there, which
is luck rather than a property of the method.

**Not a per-channel charset map.** `CHARSET-BANNER`, `CHARSET-GMCP` and the rest are surface for
channels no measurement has asked about, and they invite an operator to state things nobody knows.
Two channels are what the evidence names.

**Not a change to the default path.** The session-wide decision exists for a documented reason — a
game with an ASCII connect screen and a GBK name in its report would otherwise be certified UTF-8 on
the strength of the screen, and its name read with an encoding nothing tested. That argument is
still good. It is the reason the report's bytes vote, and the vote stays wherever nobody has
overridden the report.

## The change

`ProbeTarget` gains `MsspCharset`, beside `Charset` and for the same reason: a fact about one game,
not the whole crawl. `WireEncoding.Read` gains a fourth argument for it and `WireReading` a second
encoding, `MsspEncoding`, which the probe uses in place of `Encoding` when it builds `MsspReport`.

With no `CHARSET-MSSP` set, `MsspEncoding` **is** `Encoding` and every byte decodes exactly as it
does today. The new field changes nothing for the other ~320 games in the registry.

With one set, two things happen:

1. The report's values decode with the named encoding, whatever the screen settled on.
2. **The report's bytes stop voting on the screen's encoding.**

The second is a narrow reversal of the session-wide rule and is load-bearing. The vote exists to
protect the report from being read with an untested encoding. Once the report has a declared
encoding, its bytes are no longer evidence about the screen — and leaving the vote in would mean an
operator who fixes a game's name silently drags its connect screen to Latin-1. The `IsUtf8`
conjunction becomes conditional on there being no report override; nothing else in `Read` moves.

An unrecognised name behaves as `Charset` already does: `Override` returns null, and the session
reads the ordinary way rather than failing. A typo in an operator's line must not cost a probe.

## The field

`CHARSET-MSSP`, registered in `FieldRegistry` beside `CHARSET`, with the same refresh window and not
owner-writable. The suffix follows `DESCRIPTION-DE` rather than introducing a prefix form.

Registration is what makes it settable through the existing `game_field_set` MCP tool and
`mui-crawl`, so no new tooling ships with it. `NpgsqlCrawlTargetRepository` reads the `staff` row for
it alongside the `CHARSET` one, in the same query, and `CrawlCycle` carries it onto the target.

No measured counterpart. Under operator-stated-only there is nothing measured to record, and
`charset.read` already carries what the session's bytes proved.

## Testing

Against the real bytes above, in `WireEncodingTests` beside the existing `pkuxkx` fixture:

- A report override decodes the report with the named encoding while the screen keeps its own —
  `doom`'s two byte strings, one call, both correct.
- With no report override, `MsspEncoding` is the session encoding, for all three of `Proven`,
  `Overridden` and `Undetermined`.
- A report override stops the report's bytes voting: an ASCII screen with a GBK report and
  `CHARSET-MSSP = gbk` reads the screen as UTF-8, where today the same input reaches Latin-1.
- Without the override that input still reaches Latin-1 — the documented behaviour, pinned so this
  change cannot quietly widen.
- An unrecognised report override falls through to the session's encoding rather than throwing.

### And at the probe

`AGameWhoseReportIsNotInItsScreensEncodingIsReadCorrectlyInBoth` drives the whole path against a
fixture serving a Big5 screen and a UTF-8 report: the screen reads `big5`, the report reads
`Doom of Lost Kingdoms (失落的國度)`.

Its value is what it does *without* the override — it reproduces the production symptom exactly,
`Doom of Lost Kingdoms (憭梯??摨?`, the same string the catalogue carried for eighteen days. So the
test fails for the real reason rather than an invented one.

Note that MSSP values leave a fixture through the telnet library's own encoder, which is UTF-8 here,
while the connect screen is written as Latin-1 bytes. The screen's fixtures use a Latin-1 string to
place exact bytes on the wire; a report's cannot, and a first attempt that did produced a UTF-8
encoding of Latin-1 mojibake and failed for a reason that had nothing to do with this change.

Verified against the real server too, which is what actually settles it:
`mui-probe doom.twmuds.com 4000` reads `iso-8859-1 (Undetermined)` and the mangled name; with
`big5 utf-8` it reads `big5 (Overridden)` and `NAME = Doom of Lost Kingdoms (失落的國度)`. `mui-probe`
takes the report override as a fourth argument for exactly this.

## Afterwards

`doom-of-lost-kingdoms` and `xiyang-zaixian-yidai-zongshi` currently display correctly because their
`NAME` was hand-set to the value read off the wire; only their `mssp` rows are wrong. Setting
`CHARSET-MSSP = utf-8` on both makes the measured row agree with the staff one, at which point the
`NAME` overrides could be withdrawn and the games' own reports would carry their names again. That
is an operator action, not part of this change.
