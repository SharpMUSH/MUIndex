---
kind: client
slug: tintin
title: TinTin++
summary: 独自のスクリプト言語を持つターミナルのクライアント。電話機を含むあらゆるプラットフォームで動き、文書化されたスクリーンリーダーモードがあります。
home: https://tintin.mudhalla.net/
platform: Linux
platform: macOS
platform: Windows
platform: Android
platform: iOS
capability: screen reader | yes | https://tintin.mudhalla.net/manual/screen_reader.php
capability: TLS | yes | https://github.com/scandum/tintin
capability: UTF-8 | yes | https://github.com/scandum/tintin
capability: MCCP | yes | https://tintin.mudhalla.net/
capability: GMCP | yes | https://tintin.mudhalla.net/manual/event.php
capability: MSDP | yes | https://tintin.mudhalla.net/manual/msdp.php
capability: ATCP | unknown |
capability: MXP | unknown |
capability: MSP | unknown |
capability: scripting | yes | https://github.com/scandum/tintin
see-also: clients/blightmud
see-also: clients/mudlet
see-also: protocols/msdp
see-also: protocols/ttype
---

TinTin++はコマンドラインのクライアントで、GPL 3、活発にリリースされており、ここにある何よりも多くの
場所で動きます — AndroidとiOSも含めて。スクリプト言語は独自のもので、簡潔で、非常に多くのことが
できます。他のクライアントがGUIでやることのかなりの部分が、ここでは `#config` の1行です。

同じ作者が**MSSP**と**MSDP**のプロトコル仕様を維持しており、この節のプロトコルのページの多くが同じ
サイトを典拠にしているのは、そのためです。

## アクセシビリティ

TinTin++には**スクリーンリーダーモード**（`#config screen reader on`、または起動時の `-s`）のための
専用のマニュアルページがあります。有効にすると2つのことが起きます。読み上げても意味をなさない視覚的な
要素を取り除くか変えること、そしてスクリーンリーダーの使用を[MTTS](/reference/protocols/ttype)を通じて
サーバーへ伝えることで、ゲームは自分の出力を合わせられます。

これは文書化されたモードであって、特定のリーダーで試験したという主張ではありません — そのページに
製品名は挙がっていません。動作するリーダーを名指しするクライアントよりは明確に弱い証拠であり、何も
ないよりは明確に強い証拠です。

## 表が不明としている箇所

**MXP**と**MSP**はどちらも、プロジェクトのサイトにコミュニティのスクリプトがありますが、スクリプトが
あることはクライアントがそのプロトコルに対応していることではありません — MXPのものは、すべてのMUDで
動くとは限らないとはっきり述べています。どちらについても、ネイティブの対応は確かめられませんでした。
**ATCP**はどちらとも何も見つかりませんでした。なお、ATCPはおおむねGMCPに取って代わられており、GMCPには
TinTin++が対応しています。
