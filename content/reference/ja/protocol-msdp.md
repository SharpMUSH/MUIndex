---
kind: protocol
slug: msdp
title: MSDP
summary: Mud Server Data Protocol — GMCPと同じ仕事を、コンパクトなバイナリ符号化と、GMCPにはない探索の仕組みでこなします。
protocol: MSDP
home: https://www.mudhalla.net/tintin/protocols/msdp/
see-also: protocols/gmcp
see-also: clients/tintin
see-also: clients/blightmud
---

MSDPはtelnetオプション69で、[GMCP](/reference/protocols/gmcp)と同じ問題を解きます。テキストと並べて
構造化データを送り、クライアントが文章から数値を拾い集めずに済むようにする、というものです。

違いは2つあります。MSDPの符号化は**バイナリでコンパクト**であり — 変数と値はJSONで包むのではなく、
1バイトの制御文字で標されます — さらにMSDPは**探索**のやり取りを定めています。クライアントは
`COMMANDS`や`REPORTABLE_VARIABLES`などを`LIST`で問い合わせ、そのゲームが何に対応しているかを教えて
もらえます。GMCPに相当するものはなく、だからGMCPのクライアントは、たいていゲームごとに設定してやる
必要があります。

実際には普及ではGMCPが勝ち、MSDPは実装したサーバーとクライアントに、しばしばGMCPと並んで残って
います。

## 当サイトが実測するもの

ここに数えられるのは、当サイトが観測したハンドシェイクで、そのゲームのサーバーがMSDPを提供した場合
です。このセクションのどの数値とも同じで、これは肯定的な観測であり、残りがその反対だということには
なりません — 数えられていないゲームは、MSDPを実装していないのかもしれませんし、単にまだ当サイトが
そのハンドシェイクを読み取っていないだけかもしれません。
