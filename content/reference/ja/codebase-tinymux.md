---
kind: codebase
slug: tinymux
title: TinyMUX
summary: もう一つの大きなMUSHサーバー。PennMUSHのものと議論になるほど近いsoftcode、MSSPはまったくなし、そして機能するログイン前のWHO。
codebase: TinyMUX
home: https://www.tinymux.org/
see-also: codebases/pennmush
see-also: codebases/tinymush
see-also: codebases/rhostmush
see-also: mush-mud-muck-moo
---

TinyMUXは、定着したロールプレイMUSHの多くが動かしている2つのサーバーのうちの2番目で、多くのプレイヤーに
とって、これとPennMUSHのどちらを選ぶかは、そのゲームのスタッフがどちらを先に覚えたかの問題です。
バージョンは `2.12` のような形で読めます。

PennMUSHと同じくTinyMUSHの子孫であり、そのsoftcodeは、両者を行き来するビルダーが学び直すのではなく
翻訳をしていると言えるほど近いものです。違いは実在します — 関数ライブラリ、いくつかの解釈の細部、
`@` コマンド群 — そしてそれこそ、データベースを両者の間で移すことをエクスポートではなくプロジェクトに
してしまうたぐいのものです。

## 外から見たときの姿

**MSSPはありません。** TinyMUXはこのオプションをまったく提供せず、そのためAresMUSH、MUCK、RhostMUSH、
CobraMUSH、TinyMUSHとともに、MSSPだけを見るディレクトリにはそもそも見えない、この趣味の側に立っています。
その接続数はログイン画面の `WHO` から来ており、素の数値で答えます。

CHARSETはネゴシエートします。非ASCIIのテキストで親戚の大半より優位に立つのは、そのおかげです。

## 接続数の出どころ

TinyMUXのゲームについて当サイトの数値を別のディレクトリのものと比べるなら、当サイトはログイン画面の
`WHO` を読んでおり、たいていのクローラーはそれを読まない、という点に注意してください。MSSPだけを土台に
したディレクトリは、こうしたゲームを接続数がまったくないものとして報告するか、そもそも一覧に載せません。
