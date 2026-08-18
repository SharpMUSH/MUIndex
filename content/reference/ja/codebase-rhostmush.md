---
kind: codebase
slug: rhostmush
title: RhostMUSH
summary: 深い権限モデルと大きな組み込み関数群で知られるMUSHサーバー。MSSPはなく、ログイン前のWHOに応答します。
codebase: RhostMUSH
home: https://github.com/RhostMUSH/trunk
see-also: codebases/pennmush
see-also: codebases/tinymux
see-also: codebases/cobramush
---

RhostMUSHは、TinyMUSHから派生した広く使われているサーバーの4番目であり、管理のモデルが最も精緻なもの
です。権限とフラグのシステムは親戚たちよりかなり細かい粒度を持っており、ゲームがこれを選ぶ理由は
たいていそこにあります。

組み込みの関数ライブラリは大きく、Rhost向けに書かれたsoftcodeは、他のサーバーにはない関数を使った部分を
書き直さないかぎり、PennMUSHやTinyMUXへきれいには移植できないことがよくあります。

## 外から見たときの姿

MSSPはありません。ログイン前の `WHO` があり、数値で答えます。CHARSETはネゴシエートされます。

この組み合わせ — MSSPはなく、`WHO` は機能する — はMUSHファミリーの署名であり、当サイトがそもそも
ログイン画面を探査する理由です。当サイト自身の調査が示す限り、MSSPのファミリーと `WHO` のファミリーは
ほぼ交わりません。28のコードベースがMSSPを通じて接続数を公開し、7つが `WHO` を通じて公開し、
両方を通じて公開するのは2つだけです。
