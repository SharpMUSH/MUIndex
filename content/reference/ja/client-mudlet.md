---
kind: client
slug: mudlet
title: Mudlet
summary: クロスプラットフォームでLuaによるスクリプトを備え、この節でスクリーンリーダー対応が最も丁寧に文書化されているクライアント。
home: https://www.mudlet.org/
platform: Windows
platform: macOS
platform: Linux
capability: screen reader | yes | https://wiki.mudlet.org/w/Manual:Screen_Readers
capability: TLS | yes | https://wiki.mudlet.org/w/Manual:Supported_Protocols
capability: UTF-8 | yes | https://wiki.mudlet.org/w/Manual:Supported_Protocols
capability: MCCP | unknown |
capability: GMCP | yes | https://wiki.mudlet.org/w/Manual:Supported_Protocols
capability: MSDP | yes | https://wiki.mudlet.org/w/Manual:Supported_Protocols
capability: ATCP | yes | https://wiki.mudlet.org/w/Manual:Supported_Protocols
capability: MXP | yes | https://wiki.mudlet.org/w/Manual:Supported_Protocols
capability: MSP | yes | https://wiki.mudlet.org/w/Manual:Supported_Protocols
capability: scripting | yes | https://github.com/Mudlet/Mudlet
see-also: clients/blightmud
see-also: clients/tintin
see-also: protocols/gmcp
see-also: connecting
---

Mudletはグラフィカルなクライアントで、マッパー、パッケージの仕組み、そして自身の機能の大半がそれに
対して書かれているLuaのAPIを備えています。GPLで、活発にリリースされており、現代の戦闘MUDを始める人への
定番の推薦です。

## アクセシビリティ

これはこの節で最も強い、文書に裏づけられた事例を持つクライアントであり、ここで「文書化されている」が
何を意味するのかを書き出しておく価値があります。珍しいことだからです。

Mudletには**スクリーンリーダーについてのマニュアルの章**があり、OSごとのページではWindowsのNarrator、
NVDA、JAWS、LinuxのOrca、macOSのVoiceOverが名指しされています。クライアント内には `mudlet access on`
というコマンドがあり、届いたゲームのテキストをリーダーを通じて読み上げる選択肢もあります。さらに、
スクリーンリーダーの使用をMTTSでサーバーへ通知する設定もあるので、望むゲームは対応を変えられます。

うまく動かないところについても率直です。自身のWindowsのページは、JAWSは他のリーダーのようには出力
ウィンドウを読まないと述べ、代わりにNarratorかNVDAを勧めています。自分のアクセシビリティ対応が実用に
ならない場合を公開するプロジェクトは、チェックマークを公開するプロジェクトより良い情報を与えて
くれます。

## 表が不明としている箇所

**MCCP**。MudletのソースはMCCPのv1とv2を実装していますが、マニュアルの対応プロトコルのページはそれを
挙げておらず、この節の規則は、機能の主張はプロジェクト自身のドキュメントを典拠にする、というものです。
ヘッダーから定数を読み出すのは同じ行為ではないので、このセルは不明としています。

## エンコーディングについての注記

Mudletの既定のサーバーデータのエンコーディングはUTF-8ではなくASCIIであり、CHARSETのネゴシエーションは
4.10で入りました。新しいプロファイルでゲームのテキストが化けるなら、まず見るべきはその設定です。
