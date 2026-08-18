---
kind: codebase
slug: cobramush
title: CobraMUSH
summary: 独自のdivisionと権限のモデルを持つPennMUSHのフォーク。稼働数は少ないものの、今も応答しています。
codebase: CobraMUSH
home: https://cobramush.org/
see-also: codebases/pennmush
see-also: codebases/rhostmush
---

CobraMUSHはPennMUSHからフォークし、*division* モデルを加えました。親が使うwizard/royaltyというフラットな
区別に代えて、委譲できる権限を備えた管理権限の階層を置くものです。スタッフの権限をすべて渡すことなく、
その一部だけを渡したいゲームが、その支持層です。

PennMUSH向けに書かれたsoftcodeはたいてい動き、違いはまさにこのフォークの主題だった領域に集中しています。

## 外から見たときの姿

MSSPはなく、ログイン前の `WHO` は機能し、実測したゲームではtelnetオプションはまったく
ネゴシエートされませんでした。最後の点は批判ではありません。何もネゴシエートしないサーバーは
ネゴシエーションを間違えようのないサーバーであり、素のソケットを流れる素のテキストは、この趣味の
どのクライアントも扱えるものです。
