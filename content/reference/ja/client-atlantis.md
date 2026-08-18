---
kind: client
slug: atlantis
title: Atlantis
summary: macOS専用のクライアント。長命で、長らくベータのままです。スクリプト機能はもう動かないと文書化されており、それがこの節で唯一の正直な「いいえ」です。
home: https://www.riverdark.net/atlantis/
platform: macOS
capability: screen reader | unknown |
capability: TLS | yes | https://www.riverdark.net/atlantis/history.php
capability: UTF-8 | yes | https://www.riverdark.net/atlantis/history.php
capability: MCCP | yes | https://www.riverdark.net/atlantis/history.php
capability: GMCP | unknown |
capability: MSDP | unknown |
capability: ATCP | unknown |
capability: MXP | unknown |
capability: MSP | unknown |
capability: scripting | no | https://www.riverdark.net/atlantis/
see-also: clients/mudlet
see-also: protocols/charset
---

AtlantisはmacOSのネイティブなクライアントで、Mac OS X 10.3の頃から存在し、Catalinaの時代に64ビットへ
更新されました。RFC 2066の文字セットのネゴシエーションとUnicodeを扱い、これはその年齢から想像される
よりも優れています。MCCPとSSLにも対応しています。

## この節で唯一の「いいえ」

スクリプト機能はCamelBonesのブリッジを介したPerlでしたが、プロジェクト自身のホームページが、それはもう
動かないと述べています — AppleによるPerlの扱いが変わり、ライブラリの作者は数年前に亡くなりました。
これは*出典のある不在*であって、不明とは別のものです。そしてクライアントの節全体で、それを持つセルは
ここだけです。ほかのどこでも、正直な答えは「確かめられなかった」でした。

## 確かめられなかったすべて

バージョンの履歴は完全な形で公開されており、**MCCP**、**SSL**、**文字セットのネゴシエーション**に
触れています — そしてGMCP、MSDP、ATCP、MSPには一度も触れていません。MXPは一度だけ、1.0.0より後の
バージョンに向けたものとして現れますが、そのバージョンは来ていません。

スクリプトのAPIにはPerlの `Atlantis::Speak()` という呼び出しがあり、これをスクリーンリーダー対応と
読むのは簡単でしょう。そうではありません。プロジェクト自身が動かないと述べているスクリプトの仕組みの
中の、スクリプトから呼ぶ音声読み上げです。VoiceOver、「accessible」、「screen reader」のいずれも、
ホームページにも、ダウンロードのページにも、完全なバージョン履歴にも、アーカイブされたユーザーガイドにも
現れません。

現在のダウンロードは0.9.9.8で、名目上はまだベータであり、リリース日はサイトのどこにも公開されて
いません。
