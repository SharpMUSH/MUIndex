---
kind: codebase
slug: muck
title: MUCK
summary: TinyMUD 的一支后裔，有自己的类 Forth 游戏内语言，社群文化也与 MUSH 那一边不同。
codebase: MUCK
home: https://www.fuzzball.org/
see-also: mush-mud-muck-moo
see-also: codebases/tinymush
see-also: codebases/moo
---

MUCK——实际上几乎总是指 **Fuzzball MUCK**——是 MUSH 一脉的兄弟，而不是它的后代：两者都源自 TinyMUD，
也都在游戏里放了一门编程语言。

语言就是那处看得见的差别。MUF（*Multi-User Forth*）是基于栈的，读起来和 MUSH softcode 毫无相似之
处；精通其中一门的建造者，在另一门面前是新手。在它之上还有 MPI，一门更小的内联表达式语言，用来做
在 MUSH 上会交给 softcode 去做的那些事。

从文化上说，MUCK 是这个爱好中社交与同人世界很大一部分的家园。那些游戏往往围绕在场与交谈来构建，而
不是围绕有开始有结束的场景，这是与扮演 MUSH 传统之间一处实实在在的差别，而不是题材问题。

## 从外面看是什么样

没有 MSSP。登录前的 `WHO` 会给出一个计数。在我们实测的那个游戏上没有协商任何 telnet 选项——调查中
还有一个细节值得记住：它的 `WHO` 回应以一个尾随空格结束，没有换行，而这正是那种会让一个天真的解析
器什么也报不出来的东西。
