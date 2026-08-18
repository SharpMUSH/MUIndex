---
kind: client
slug: blightmud
title: Blightmud
summary: Rustで書かれた現代的なターミナルのクライアント。Luaのスクリプト、内蔵の音声読み上げ、そしてサーバーに自らを知らせるスクリーンリーダーモードを備えています。
home: https://github.com/Blightmud/Blightmud
platform: Linux
platform: macOS
platform: Windows (WSL only)
capability: screen reader | yes | https://github.com/Blightmud/Blightmud
capability: TLS | yes | https://github.com/Blightmud/Blightmud
capability: UTF-8 | yes | https://github.com/Blightmud/Blightmud
capability: MCCP | yes | https://github.com/Blightmud/Blightmud
capability: GMCP | yes | https://github.com/Blightmud/Blightmud
capability: MSDP | yes | https://github.com/Blightmud/Blightmud
capability: ATCP | unknown |
capability: MXP | unknown |
capability: MSP | unknown |
capability: scripting | yes | https://github.com/Blightmud/Blightmud
see-also: clients/tintin
see-also: clients/mudlet
see-also: protocols/ttype
---

BlightmudはRustで書かれたターミナルのクライアントで、GPL 3、そしてこの節で最も活発にリリースされて
いるクライアントの一つです。スクリプトはLuaです。ターミナル専用で、ネイティブのWindowsビルドはなく、
Windowsの利用者はWSLの下で動かします。

## アクセシビリティ

Blightmudにはここで3つの別々の要素があり、これは表の1行が担える以上のものです。

- **スクリーンリーダーに配慮したモード**（`--reader-mode`、または `reader_mode` の設定）。ターミナルの
  UIを、リーダーが追える形に変えます。ステータス領域には対応していません。
- **内蔵の音声読み上げ**。オプションのコンパイルとして提供され、スクリプトから使えるLuaのAPIが付きます
  — 一致した行が読み上げられないようにする `tts.gag()` もあります。ドキュメントは、このTTSを
  スクリーンリーダーと並べて動かすのが常に幸せな組み合わせとは限らない、と率直に書いています。
- **MTTSの自動通知**。リーダーモードのとき、またはTTSを有効にしているとき、サーバーへ伝える自分自身の
  情報に `MTTS_SCREEN_READER` を加えるので、気にかけるゲームは対応を変えられます。

TinTin++と同じく、特定のスクリーンリーダーは名指しされていないので、これは文書化されたモードであって、
ある製品との互換性が試験されたということではありません。

## 表が不明としている箇所

**MXP**、**MSP**、**ATCP**は、プロジェクトのREADMEにも同梱のヘルプにもまったく現れません。**MCCP**は
v2として文書化されており、v1も扱えるかどうかは確かめられませんでした。
