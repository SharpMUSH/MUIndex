---
kind: codebase
slug: muck
title: MUCK
summary: Forth風の独自のゲーム内言語を持つTinyMUDの子孫。MUSH側とは異なる社交の文化を持っています。
codebase: MUCK
home: https://www.fuzzball.org/
see-also: mush-mud-muck-moo
see-also: codebases/tinymush
see-also: codebases/moo
---

MUCK — 実際にはほぼ常に**Fuzzball MUCK**のこと — は、MUSHの系統の子孫ではなくその兄弟です。どちらも
TinyMUDから来ており、どちらもゲームの中にプログラミング言語を置いています。

目に見える違いはその言語です。MUF（*Multi-User Forth*）はスタック指向で、MUSHのsoftcodeとはまるで読み味が
違います。一方に堪能なビルダーも、他方では初心者です。その上には、MUSHならsoftcodeがやることに使われる、
より小さなインラインの式言語MPIが載っています。

文化の面では、MUCKはこの趣味の社交とファンダムの世界の大きな部分の本拠地です。そうしたゲームは、
始まりと終わりのあるシーンではなく、その場にいることと会話を中心に組み立てられる傾向があり、これは
ロールプレイMUSHの伝統との実際の違いであって、テーマの問題ではありません。

## 外から見たときの姿

MSSPはありません。ログイン前の `WHO` があり、数値で答えます。実測したゲームではtelnetオプションは
ネゴシエートされませんでした — そして調査から、覚えておく価値のある細部が一つあります。その `WHO` の
返答は末尾が空白で終わり、改行がありませんでした。素朴なパーサーが何も報告しなくなるのは、こういうものが
原因です。
