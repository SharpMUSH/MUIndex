---
kind: codebase
slug: tinymux
title: TinyMUX
summary: 另一个大牌 MUSH 服务器。softcode 与 PennMUSH 的接近到足以让人争论不休，完全没有 MSSP，而登录前的 WHO 是好用的。
codebase: TinyMUX
home: https://www.tinymux.org/
see-also: codebases/pennmush
see-also: codebases/tinymush
see-also: codebases/rhostmush
see-also: mush-mud-muck-moo
---

TinyMUX 是老牌扮演 MUSH 最常用的两个服务器中的第二个，而对许多玩家来说，在它和 PennMUSH 之间怎么选
只取决于自家游戏的管理人员先学会了哪一个。版本号形如 `2.12` 之类。

和 PennMUSH 一样，它也出自 TinyMUSH，它的 softcode 接近到让一个在两者之间迁移的建造者是在做翻译，
而不是重新学。差异是实实在在的——函数库、若干解析上的边角、`@` 命令集——而且恰恰是那种会让在两者之
间搬数据库变成一个项目、而不是一次导出的东西。

## 从外面看是什么样

**没有 MSSP。**TinyMUX 根本不提供这个选项，这把它和 AresMUSH、MUCK、RhostMUSH、CobraMUSH 以及
TinyMUSH 一起归到了这个爱好中一个只认 MSSP 的目录压根看不见的那一边。它的玩家人数来自登录画面上的
`WHO`，它会用一个朴素的计数作答。

它确实会协商 CHARSET，这也是它在非 ASCII 文本上胜过大多数亲戚的原因。

## 这些人数从哪里来

如果你要拿本站给某个 TinyMUX 游戏的数字去和另一个目录的比，请注意我们读的是登录画面上的 `WHO`，而
大多数爬虫不读。一个只建立在 MSSP 之上的目录，会把这些游戏报成完全没有人数，或者干脆不把它们列出
来。
