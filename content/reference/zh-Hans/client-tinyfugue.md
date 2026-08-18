---
kind: client
slug: tinyfugue
title: TinyFugue
summary: 经典的 UNIX 终端客户端。上游自 2007 年起就没有再发布过；一个仍在维护的分支把它带了下去。
home: https://tinyfugue.sourceforge.net/
platform: Linux
platform: macOS
platform: BSD
capability: screen reader | unknown |
capability: TLS | yes | https://tinyfugue.sourceforge.net/
capability: UTF-8 | unknown |
capability: MCCP | yes | https://tinyfugue.sourceforge.net/
capability: GMCP | unknown |
capability: MSDP | unknown |
capability: ATCP | unknown |
capability: MXP | unknown |
capability: MSP | unknown |
capability: scripting | yes | https://tinyfugue.sourceforge.net/
see-also: clients/tintin
see-also: clients/blightmud
---

TinyFugue——“tf”——是 MUSH 世界很大一部分人用了二十年的终端客户端，输入和输出分成两个窗格，有一门自
己的宏语言，还有一套比它好几个竞争者活得更久的使用习惯。

**上游处于休眠状态**：最后一个发布是 5.0 beta 8，2007 年 1 月。它仍然能编译，也仍然能用。

一个仍在维护的分支 *TinyFugue Rebirth* 发布活跃，在原生宏语言之外加入了 GMCP、ATCP、经由 ICU 的宽
字符支持，以及 Python 和 Lua 脚本。上面那张表描述的是**上游**，因为“TinyFugue”指向的就是上游；如果
你今天要装，那个分支值得先看一眼。

## 这个客户端的文档里的陷阱

上游有一个叫**“non-visual mode”**的文档条目。它跟辅助技术无关——它讲的是把输入限制在最底下那一
行——而且从头到尾没有提到屏幕阅读器、语音，也没有提到盲人用户。一张靠关键词搜索拼出来的能力表，会
把那个文件名变成一个“有”。这一张写的是未知，因为文档所能支撑的只有这个。

UTF-8 是同样形状的答案：有文档的编码支持是针对 8 位的 ISO 8859 字符集，而关于 UTF-8，我们无论有无
都没有找到上游的任何说法。
