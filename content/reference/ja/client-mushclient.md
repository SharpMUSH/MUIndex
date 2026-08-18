---
kind: client
slug: mushclient
title: MUSHclient
summary: 長く定着しているWindowsのクライアント。5つのスクリプト言語、プロトコル対応の大半が置かれているプラグインのアーキテクチャ、そして緩やかになったリリースの歴史。
home: https://www.mushclient.com/
platform: Windows
platform: Linux (Wine)
capability: screen reader | unknown |
capability: TLS | unknown |
capability: UTF-8 | unknown |
capability: MCCP | yes | https://www.mushclient.com/mushclient/mccp.htm
capability: GMCP | unknown |
capability: MSDP | unknown |
capability: ATCP | yes | https://www.mushclient.com/gmcp
capability: MXP | yes | https://www.mushclient.com/mushclient/doc/general/features.html
capability: MSP | yes | https://github.com/nickgammon/mushclient/blob/master/plugins/msp.xml
capability: scripting | yes | https://www.mushclient.com/mushclient/doc/general/features.html
see-also: clients/mudlet
see-also: clients/potato
see-also: protocols/mccp
---

MUSHclientはNick GammonによるWindowsのクライアントで、MITライセンス、そして長い期間、Windowsを使う人に
とっての既定の答えでした。Lua、VBScript、JScript、PerlScript、Pythonでスクリプトが書け、その機能の
多くはコアではなくプラグインが担っています — これは本物のアーキテクチャ上の選択であり、上の表の
いくつもの行が見た目より答えにくい理由でもあります。

最後にタグの付いたリリースは**2019年3月の5.06**です。リポジトリには今もコミットが続いており、出荷されて
いない5.07のリリースノートも存在します。

## これほど多くの行が不明としている理由

どれも正直な答えが「確かめられなかった」という場合であり、その理由はそれぞれ違います。

- **GMCP** — プロジェクト自身のこれについてのページが示しているのは、あなたが書けるであろう*例*としての
  プラグインであって、クライアントが持つ機能ではありません。それは対応を出荷することとは違うので、
  このセルは「はい」ではなく不明です。
- **TLS** — 文書化されている方法は外部の `stunnel` プロセスです。OpenSSLによるTLSを加えるコミットは
  2026年にmasterブランチへ入りましたが、どのリリースにも含まれていないので、今日利用者がインストール
  できるもので指し示せるものがありません。
- **UTF-8** — CHARSETのネゴシエーションは未リリースの5.07のノートに現れますが、出荷済みのバージョンの
  ドキュメントには、当サイトが探した限りどこにもありません。
- **MSDP** — どちらとも何もありません。
- **スクリーンリーダー** — WindowsのSAPIを使う音声読み上げのプラグインがクライアントに同梱されて
  いますが、それはスクリーンリーダー対応と同じものではありません。マニュアルにアクセシビリティの節は
  なく、作者は自身のフォーラムで、出力ウィンドウがリーダーにとって扱いにくい理由を説明しています。
  現在行という概念がないのです。答えを確かめられなかったので、表も答えを出していません。

このどれも*いいえ*ではありません。いくつかは十分に「はい」でありうるのに、当サイトがそれを示せなかった
というだけです。
