---
kind: codebase
slug: evennia
title: Evennia
summary: 一个 Python 框架，而不是一个做好的游戏。两个 Evennia 游戏之间，可能除了管道什么都不共享。
codebase: Evennia
home: https://www.evennia.com/
see-also: codebases/aresmush
see-also: collaborative-roleplay
see-also: protocols/gmcp
---

Evennia 是一个 **MU\* 框架**，不是一个游戏——这是关于它首先要知道的事，也正是这一点让互相比较各个
Evennia 游戏变得没什么意义。它是一个建立在 Django 和 Twisted 之上的 Python 库，为你提供账户、对
象、房间、命令、一层持久化和整套网络栈，然后指望你把游戏写出来。

由此带来的结果是，“运行 Evennia”所告诉你的东西，远少于“运行 PennMUSH”。Evennia 上有战斗 MUD，也有
扮演游戏，它们不共享任何词汇。两个 Evennia 游戏可能连一条相同的命令都没有。

对一个已经会 Python 的开发者来说，这是从零到一个能跑的世界的最短路径，二〇一〇年代中期以来相当一
部分新游戏也正是从这里起步的。

## 从外面看是什么样

Evennia 提供 **MSSP**，并通过它发布玩家人数。在我们实测的那个游戏上，它还协商了 **MCCP2**——压
缩——这是一个认真对待自己 telnet 实现的技术栈的特征。

因为 Evennia 是一个框架，某个具体游戏协商什么，有一部分是那个游戏自己的决定。协议页面上的采用数字
数的是服务器实际提供给我们的东西，而不是这个框架能做到的东西，对 Evennia 来说，这两者之间的距离比
大多数情况都要远。
