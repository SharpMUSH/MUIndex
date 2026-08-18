---
kind: codebase
slug: fluffos
title: FluffOS
summary: 仍在维护的 MudOS 后继者，也是幸存的 LPMud 游戏大多所用的驱动。游戏是用 LPC 写的，不是用 C。
codebase: FluffOS
home: https://www.fluffos.info/
see-also: codebases/dikumud
see-also: mush-mud-muck-moo
---

LPMud 传统对世界的切分方式与 Diku 不同。这里有一个**驱动**——一个运行面向对象解释器的 C 程序——还
有一个 **mudlib**，那才是整个游戏，用 **LPC** 写成，由驱动加载。房间、战斗、命令和登录流程全都是
mudlib 对象；驱动对它们一无所知。

这使得一个 LPMud 在气质上比它的战斗系统所暗示的更接近 MUSH：游戏是用一种活在游戏内部的语言写的，
而两个共用同一个驱动的 LPMud，可能别的什么都不共享。

**MudOS** 多年来一直是主流驱动；**FluffOS** 是它仍在维护的延续，也是今天一个还在运行的 LP 游戏最可
能跑在上面的东西。有名的 mudlib——Nightmare、Lima、Discworld 自己的那套——则又是各自独立的项目。

## 从外面看是什么样

在我们实测的那个 FluffOS 游戏上有 MSSP 和 **MCCP2**。在我们的调查中，MudOS 是仅有的两个既回应
MSSP、又回应登录画面 `WHO` 的代码库之一，不过它给出的 `WHO` 是一份逐个玩家的清单，而不是一个计数。

因为 mudlib 就是游戏，任何一个具体的 LP 游戏协商什么，是 mudlib 的决定不亚于是驱动的决定——协议页
面上的采用数字数的是服务器实际提供给我们的东西，对这个家族而言，那作为关于代码库的信号，要比在别
处更弱。
