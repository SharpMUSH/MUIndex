---
kind: client
slug: mudlet
title: Mudlet
summary: 跨平台，用 Lua 编写脚本，也是本节中屏幕阅读器支持的文档写得最详尽的客户端。
home: https://www.mudlet.org/
platform: Windows
platform: macOS
platform: Linux
capability: screen reader | yes | https://wiki.mudlet.org/w/Manual:Screen_Readers
capability: TLS | yes | https://wiki.mudlet.org/w/Manual:Supported_Protocols
capability: UTF-8 | yes | https://wiki.mudlet.org/w/Manual:Supported_Protocols
capability: MCCP | unknown |
capability: GMCP | yes | https://wiki.mudlet.org/w/Manual:Supported_Protocols
capability: MSDP | yes | https://wiki.mudlet.org/w/Manual:Supported_Protocols
capability: ATCP | yes | https://wiki.mudlet.org/w/Manual:Supported_Protocols
capability: MXP | yes | https://wiki.mudlet.org/w/Manual:Supported_Protocols
capability: MSP | yes | https://wiki.mudlet.org/w/Manual:Supported_Protocols
capability: scripting | yes | https://github.com/Mudlet/Mudlet
see-also: clients/blightmud
see-also: clients/tintin
see-also: protocols/gmcp
see-also: connecting
---

Mudlet 是一个图形客户端，带地图器、一套包系统，以及一个 Lua API——它自己的大部分功能都是针对这个
API 写出来的。它是 GPL，发布活跃，也是给一个刚要开始玩现代战斗 MUD 的人的常规推荐。

## 无障碍

这是本节中有文档支撑的理由最强的客户端，而这里的“有文档”是什么意思值得说清楚，因为它并不常见。

Mudlet 有**一章讲屏幕阅读器的手册内容**，有按操作系统分开的页面，点名了 Windows 上的 Narrator、
NVDA 和 JAWS，Linux 上的 Orca，以及 macOS 上的 VoiceOver；有一条客户端内的 `mudlet access on` 命
令，还有一个通过阅读器播报游戏来文的选项。它另有一个设置，会通过 MTTS 向服务器通告正在使用屏幕阅
读器，这样游戏若愿意就可以做出调整。

它对自己哪里做得不好也很坦白：它自己的 Windows 页面说 JAWS 读输出窗口的方式和别的阅读器不一样，并
建议改用 Narrator 或 NVDA。一个把自己无障碍支持行不通的情形也公布出来的项目，给你的信息比一个只公
布一个对勾的项目要好。

## 表里写着未知的地方

**MCCP。**Mudlet 的源码实现了 MCCP v1 和 v2，但手册的支持协议页面没有把它列出来，而本节的规则是：
一项能力主张要引用项目自己的文档。从头文件里读出一个常量不是同一回事，所以这个格子写的是未知。

## 关于编码的说明

Mudlet 默认的服务器数据编码是 ASCII 而不是 UTF-8，CHARSET 协商是 4.10 才有的。如果在一个全新的配置
档上某个游戏的文本显示不对，那个设置是第一个该看的地方。
