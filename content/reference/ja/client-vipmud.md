---
kind: client
slug: vipmud
title: VIP Mud
summary: 目の見えないプレイヤーのために一から作られた商用のWindowsクライアント。7つのスクリーンリーダーを名指しする一方で、プロトコル対応についてはほとんど何も公開していません。
home: https://www.gmagames.com/vipmud.shtml
platform: Windows
capability: screen reader | yes | https://www.gmagames.com/vipmud.shtml
capability: TLS | unknown |
capability: UTF-8 | unknown |
capability: MCCP | unknown |
capability: GMCP | unknown |
capability: MSDP | unknown |
capability: ATCP | unknown |
capability: MXP | unknown |
capability: MSP | yes | https://www.gmagames.com/vipmud.shtml
capability: scripting | yes | https://www.gmagames.com/vipmud.shtml
see-also: clients/mudlet
see-also: clients/blightmud
---

VIP Mudは、この節で設計の前提が*まるごと*アクセシビリティであるただ一つのクライアントです。商用で —
30ドル、30日間は全機能を試用でき、その後も機能を減らした形で動き続けます — Windowsのプログラムです。

ここでは群を抜いて強いアクセシビリティの主張であり、しかも珍しく具体的です。製品ページは**JAWS、
Window-Eyes、System Access、NVDA、Cobra、SuperNova/Hal、そしてMicrosoft SAPI**を、そのままで動作する
ものとして名指しし、この問題を真剣に考えた人でなければ出てこない機能を説明しています。ウィンドウごと・
出力の種類ごとに異なる声、スパムを表示はしたまま読み上げからは外すこと、そしてASCIIアートを抑える
いくつもの方法 — ASCIIアートは、MUDがスクリーンリーダーへ送るもののなかで最も敵対的なものです。

## 表の残りが空である理由

ベンダーが公開しているのがマニュアルではなくマーケティングのページだからです。そこにはGMCP、MSDP、
MCCP、MXP、ATCP、TLS、文字エンコーディングへの言及が一つもありません。製品を「a Telnet-based client」と
説明して、そこで終わりです。**不明が9つ並ぶことは、そのソフトウェアへの判定ではありません。** 利用
できる情報源が1ページしかないときに表がこう見えるというだけであり、それを9つの「いいえ」として公開
すれば、それらすべてを十分にこなしうる製品についての嘘になります。

さらに確かめられなかったことが2つあります。現行バージョンのリリース日と、今も活発に開発されているか
どうかです — ベンダーは2025年2月に買収されており、製品ページには2016年の著作権表示が載っています。
