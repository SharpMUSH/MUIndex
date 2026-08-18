---
kind: codebase
slug: circlemud
title: CircleMUD
summary: 教材に使えるほど丁寧に文書化されたDikuMUDの派生。膨大にフォークされ、今も動いています。
codebase: CircleMUD
home: https://www.circlemud.org/
see-also: codebases/dikumud
see-also: codebases/tbamud
see-also: codebases/rom
---

CircleMUDはDikuMUDの派生ですが、その際立った特徴はゲームの仕組みではありませんでした。**ドキュメント**
でした。Jeremy Elsonのリリースは整っていて、コメントが付き、コーディングガイドが添えられており、その結果、
人々がCを学び、そこからMUDを動かし、まず何かをリバースエンジニアリングしなくてもフォークできる
コードベースになりました。

その帰結として、稼働しているゲームの非常に多くが何世代も離れたCircleの派生であり、しかもプレイヤーの目に
触れる場所にはその名前がどこにも現れないことがよくあります。

Circle本体の開発はとうに終わっており、その続きが**tbaMUD**です。今日メンテナンスされているCircleの
ゲームは、たいていtbaMUDとしてメンテナンスされています。

## 外から見たときの姿

MSSPがあり、要求すれば応答します。ログイン画面に `WHO` はありません — Dikuのファミリーは一般に
それを提供しないので、ログイン画面しか読まないディレクトリはここでは何も見えません。
