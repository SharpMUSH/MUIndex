---
kind: client
slug: tinyfugue
title: TinyFugue
summary: 古典的なUNIXのターミナルクライアント。上流は2007年以降リリースしておらず、メンテナンスされているフォークがそれを前へ運んでいます。
home: https://tinyfugue.sourceforge.net/
platform: Linux
platform: macOS
platform: BSD
capability: screen reader | unknown |
capability: TLS | yes | https://tinyfugue.sourceforge.net/
capability: UTF-8 | unknown |
capability: MCCP | yes | https://tinyfugue.sourceforge.net/
capability: GMCP | unknown |
capability: MSDP | unknown |
capability: ATCP | unknown |
capability: MXP | unknown |
capability: MSP | unknown |
capability: scripting | yes | https://tinyfugue.sourceforge.net/
see-also: clients/tintin
see-also: clients/blightmud
---

TinyFugue — 「tf」 — は、MUSHの世界の大きな部分が20年にわたって使ったターミナルのクライアントで、入力と
出力の別々のペイン、独自のマクロ言語、そして競合のいくつかより長生きした一連の習慣を備えています。

**上流は休眠しています**。最後のリリースは2007年1月の5.0 beta 8です。今もビルドが通り、今も動きます。

メンテナンスされているフォーク *TinyFugue Rebirth* は活発にリリースされており、GMCP、ATCP、ICUによる
ワイド文字への対応、そして本来のマクロ言語と並ぶPythonとLuaのスクリプトを加えています。上の表が説明して
いるのは**上流**です。「TinyFugue」が指すのはそちらだからです。今日インストールするなら、まずフォークを
見てみる価値があります。

## このクライアントのドキュメントにある罠

上流のドキュメントには**「non-visual mode」**という項目があります。これは支援技術についてのものでは
なく — 入力を最下行に閉じ込めておくことに関するものです — スクリーンリーダーにも、音声にも、視覚に
障害のある利用者にも、どこにも触れていません。キーワード検索で組み立てた機能表なら、そのファイル名を
「はい」に変えてしまうでしょう。この表は不明としています。ドキュメントが裏づけているのはそこまでだから
です。

UTF-8も同じ形の答えです。文書化されているエンコーディングへの対応は8ビットのISO 8859の文字セットに
ついてのもので、UTF-8についての上流の記述は、どちらとも見つかりませんでした。
