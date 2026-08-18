---
kind: codebase
slug: pennmush
title: PennMUSH
summary: 部署最广的 MUSH 服务器。softcode、一段很长的发布史，以及在我们的调查中仅有的两个既回应 MSSP 又回应登录前 WHO 的代码库之一。
codebase: PennMUSH
home: https://www.pennmush.org/
see-also: codebases/tinymux
see-also: codebases/rhostmush
see-also: codebases/cobramush
see-also: mush-mud-muck-moo
see-also: protocols/mssp
---

PennMUSH 经由 1991 年的一次分支从 TinyMUSH 而来，也是长期运行的扮演 MUSH 最常用的服务器。它的决定
性特征是 **softcode**：一门函数式表达式语言，由任何设置了相应标记位的人从游戏内部编辑，任何一个
MUSH 的行为都有很大一部分是用它写的。与其说一个 PennMUSH 游戏是被配置出来的，不如说是被它的玩家编
程出来的。

版本号形如 `1.8.8p0`——一个主版本、一个次版本和一个补丁级别——而补丁级别经常变动。游戏常常跑在落后
好几个补丁级别的版本上，这并不稀奇。

## 从外面看是什么样

在我们自己那次涵盖 38 台服务器的调查中，PennMUSH 是仅有的两个把我们探测的*两条*路径都回应了的代码
库之一。它在被问及时提供 MSSP，也会回应在登录画面上敲入的 `WHO`，而在我们实测的那个游戏上，两者是
一致的——这比听上去要罕见，也让 PennMUSH 成了我们用来对照其他服务器的基准。

登录前的 `WHO` 的意义不止于方便：MUSH 家族能发布玩家人数，靠的就是它，因为这个家族的其余大部分根
本不提供 MSSP。这一分裂正是本站要探测四层而不是一层的原因，见
[MSSP](/reference/protocols/mssp)。

在现代 PennMUSH 上，CHARSET 协商是常态，这也是带重音符号的名字能平安走完全程的原因。

## 相关服务器

PennMUSH、**TinyMUX**、**RhostMUSH** 和 **CobraMUSH** 是四个有共同祖先、共享一套词汇的服务器——懂其
中一个的建造者，费点力气也能读懂另一个的 softcode。它们并不兼容：数据库不做一次转换是搬不过去的，
函数库的差异也大到会带来实际影响。

## SharpMUSH

有一个以 PennMUSH 兼容为目标的 .NET 重新实现正在开发中，作者与本站相同。本页没有任何内容是从它实测
出来的，目录里也没有它的游戏。
