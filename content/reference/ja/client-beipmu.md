---
kind: client
slug: beipmu
title: BeipMU
summary: この趣味のMUSH側に向けたWindowsのクライアント。出力ウィンドウにスクリーンリーダー対応があり、MXPではなくPuebloに対応します。
home: https://beipdev.github.io/BeipMU/
platform: Windows
capability: screen reader | yes | https://github.com/BeipDev/BeipMU/blob/master/Assets/Changes.txt
capability: TLS | yes | https://beipdev.github.io/BeipMU/
capability: UTF-8 | yes | https://beipdev.github.io/BeipMU/
capability: MCCP | unknown |
capability: GMCP | yes | https://github.com/BeipDev/BeipMU/blob/master/Documentation/GMCP.md
capability: MSDP | unknown |
capability: ATCP | unknown |
capability: MXP | unknown |
capability: MSP | unknown |
capability: scripting | yes | https://beipdev.github.io/BeipMU/
see-also: clients/mushclient
see-also: clients/potato
see-also: collaborative-roleplay
---

BeipMUはMITライセンスのWindowsのクライアントで、活発にリリースされており、戦闘MUDではなくMUSH風の
遊び方を念頭に作られた数少ないものの一つです — 複数の入力ウィンドウ、スポーンウィンドウ、そして長い
段落を前提としたテキストエンジン。スクリプトは既定でJavaScriptで、ほかのActiveScriptのエンジンも
使えます。

## アクセシビリティ

出力ウィンドウはWindowsの `IAccessible` インターフェースを実装しています。これは視覚に障害のある
プレイヤーにとっての使いやすさへ向けた一歩として、意図的に加えられたものです。また音声読み上げのための
**Speak**というトリガーの動作があります。特定のスクリーンリーダーはどこにも名指しされておらず、
ドキュメントにアクセシビリティの章はありません。

調べに行くなら注意が一つあります。プロジェクト自身のドキュメントのあるページは、BeipMUは音声合成を
使えないと今も述べています。そのページは古くなっています — 変更履歴も、メンテナー自身のissueへの
コメントも、どちらもそれより後のものです。

## このクライアントについて起こしやすい2つの誤り

**BeipMUが実装しているのはMCMPであって、MSPではありません。** 両者は名前も目的も似た、別のプロトコル
です。一方を他方として読めば、誰もしていない主張をこの表に載せることになります。だからMSPの行は不明と
しています。

**対応しているのはPuebloであって、MXPではありません。** PuebloはMUDの中でHTMLを使う古いほうの方式で、
MXPは後のほうです。BeipMUは基本的なPuebloのスタイルとクリックできるリンクを文書化しています。MXPに
ついては、どちらとも確かめられませんでした。
