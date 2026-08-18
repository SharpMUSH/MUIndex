---
kind: client
slug: potato
title: Potato MUSHclient
summary: MUSHのプレイヤーのために書かれた、クロスプラットフォームのTcl/Tkクライアント。エンコーディングへの対応は良好で、ドキュメントはプロトコルの大半について何も語りません。
home: https://www.potatomushclient.com/
platform: Windows
platform: Linux
platform: macOS
capability: screen reader | unknown |
capability: TLS | yes | https://github.com/potatomushclient/potato/wiki/ConfigureWorldsBasics
capability: UTF-8 | yes | https://github.com/potatomushclient/potato/wiki/Features
capability: MCCP | unknown |
capability: GMCP | unknown |
capability: MSDP | unknown |
capability: ATCP | unknown |
capability: MXP | unknown |
capability: MSP | unknown |
capability: scripting | yes | https://github.com/potatomushclient/potato/wiki/FAQs
see-also: clients/beipmu
see-also: clients/mushclient
see-also: collaborative-roleplay
---

PotatoはMUSHでの遊びのために作られたTcl/Tkのクライアントです — 複数のワールド、スポーンウィンドウ、
そして戦闘のコマンドではなくポーズを打っていることを前提にした既定値の一式。同じソースからWindows、
Linux、macOSで動き、macOSのビルドはたいてい1つか2つバージョンが遅れています。

文字エンコーディングをネゴシエートし、完全なUnicodeを話します。この趣味のMUSH側にとっては、実際上
これが最も重要な機能です。

文書化された制限が一つあります。最初からSSLであるポートへの接続には対応していますが、設定のページ自身
が、STARTTLS方式のネゴシエートされるSSLには**対応していない**と述べています。

## 6つの行が不明としている理由

当サイトは、プロジェクトのホームページ、ダウンロードのページ、103個あるウィキのヘルプファイルすべて、
そしてソースツリー全体を、GMCP、MSDP、MCCP、MXP、MSP、ATCPについて検索しました。そのどれについても、
文書化された記述はありません。いくつかに触れる*コード*はありますが、この節はコードを機能の主張には
変えません — ヘッダーにある定数を根拠に「はい」と言う表は、プロジェクトが一度もしなかった約束を
することになります。

スクリーンリーダーの行も、同じやり方でたどり着いた同じ答えです。プロジェクトが公開しているすべてを
対象に、大文字小文字を区別せず「screen reader」「text-to-speech」、NVDA、JAWS、VoiceOver、
「accessibility」「visually impaired」「blind」を掃いたところ、何も出てきませんでした。これは
ソフトウェアについての発見ではありません。
