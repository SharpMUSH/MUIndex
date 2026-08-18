---
kind: client
slug: tintin
title: TinTin++
summary: 一个有自己脚本语言的终端客户端，在每一个平台上都能跑，手机也不例外，并且有一个成文的屏幕阅读器模式。
home: https://tintin.mudhalla.net/
platform: Linux
platform: macOS
platform: Windows
platform: Android
platform: iOS
capability: screen reader | yes | https://tintin.mudhalla.net/manual/screen_reader.php
capability: TLS | yes | https://github.com/scandum/tintin
capability: UTF-8 | yes | https://github.com/scandum/tintin
capability: MCCP | yes | https://tintin.mudhalla.net/
capability: GMCP | yes | https://tintin.mudhalla.net/manual/event.php
capability: MSDP | yes | https://tintin.mudhalla.net/manual/msdp.php
capability: ATCP | unknown |
capability: MXP | unknown |
capability: MSP | unknown |
capability: scripting | yes | https://github.com/scandum/tintin
see-also: clients/blightmud
see-also: clients/mudlet
see-also: protocols/msdp
see-also: protocols/ttype
---

TinTin++ 是一个命令行客户端，GPL 3，发布活跃，而且它能跑的地方比这里的任何其他客户端都多——包括
Android 和 iOS。它的脚本语言是它自有的，很简练，能做的事情却很多；别的客户端在图形界面里做的相当
一部分事情，在这里是一行 `#config`。

**MSSP** 和 **MSDP** 的协议规范由同一位作者维护，这也是本节里那么多协议页面都引用同一个站点的原因。

## 无障碍

TinTin++ 有一个专门讲**屏幕阅读器模式**的手册页面（`#config screen reader on`，或者启动时加
`-s`）。启用它会做两件事：把念出来没有意义的视觉元素去掉或改掉，以及通过
[MTTS](/reference/protocols/ttype) 向服务器报告正在使用屏幕阅读器，这样游戏就可以调整自己的输出。

那是一个有文档的模式，不是与某个具体阅读器测试过的主张——那一页上没有点名任何产品。作为证据，它明
显弱于一个点名了自己配合哪些阅读器的客户端，也明显强于什么都没有。

## 表里写着未知的地方

**MXP** 和 **MSP** 在项目站点上都有社区脚本，而一个脚本不等于客户端支持某个协议——MXP 那个脚本直接
说了它未必能在每个 MUD 上工作。两者的原生支持都未能确立。**ATCP** 我们无论有无都没找到任何说法；顺
带一提，ATCP 大体上已被 GMCP 取代，而 GMCP 是 TinTin++ 支持的。
