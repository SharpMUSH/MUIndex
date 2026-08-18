---
kind: client
slug: potato
title: Potato MUSHclient
summary: 一个为 MUSH 玩家写的跨平台 Tcl/Tk 客户端。编码支持不错，而它的整套文档对大多数协议只字未提。
home: https://www.potatomushclient.com/
platform: Windows
platform: Linux
platform: macOS
capability: screen reader | unknown |
capability: TLS | yes | https://github.com/potatomushclient/potato/wiki/ConfigureWorldsBasics
capability: UTF-8 | yes | https://github.com/potatomushclient/potato/wiki/Features
capability: MCCP | unknown |
capability: GMCP | unknown |
capability: MSDP | unknown |
capability: ATCP | unknown |
capability: MXP | unknown |
capability: MSP | unknown |
capability: scripting | yes | https://github.com/potatomushclient/potato/wiki/FAQs
see-also: clients/beipmu
see-also: clients/mushclient
see-also: collaborative-roleplay
---

Potato 是一个为 MUSH 玩法打造的 Tcl/Tk 客户端——多个世界、spawn 窗口，以及一套假定你敲的是 pose（扮
演描述）而不是战斗命令的默认设置。它用同一份源码在 Windows、Linux 和 macOS 上运行，其中 macOS 的构
建通常落后一两个版本。

它会协商字符编码，并且完整支持 Unicode，对这个爱好中 MUSH 那一边来说，这是实践中最要紧的一项能力。

有一处有文档说明的限制值得注意：它支持连接到一个从一开始就是 SSL 的端口，而它自己的配置页面说，
STARTTLS 那种协商式 SSL **不**受支持。

## 为什么有六行写着未知

我们在项目的主页、下载页、全部 103 个维基帮助文件以及整棵源码树里都搜过 GMCP、MSDP、MCCP、MXP、
MSP 和 ATCP。关于它们中的任何一个，都没有成文的说法。确实有*代码*碰到了其中一些，而本节不会把代码
变成一项能力主张——一张仅凭头文件里的一个常量就写下“有”的表，是在做一个项目从未做过的承诺。

屏幕阅读器那一行是用同样的办法得到的同样的答案：对项目发布的一切做一次不区分大小写的搜查，找
“screen reader”、“text-to-speech”、NVDA、JAWS、VoiceOver、“accessibility”、“visually impaired”和
“blind”，结果什么也没有。这不是关于这个软件的发现。
