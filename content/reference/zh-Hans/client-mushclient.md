---
kind: client
slug: mushclient
title: MUSHclient
summary: 老牌的 Windows 客户端。五种脚本语言，一套承载了它大部分协议支持的插件架构，以及一段已经放缓的发布史。
home: https://www.mushclient.com/
platform: Windows
platform: Linux (Wine)
capability: screen reader | unknown |
capability: TLS | unknown |
capability: UTF-8 | unknown |
capability: MCCP | yes | https://www.mushclient.com/mushclient/mccp.htm
capability: GMCP | unknown |
capability: MSDP | unknown |
capability: ATCP | yes | https://www.mushclient.com/gmcp
capability: MXP | yes | https://www.mushclient.com/mushclient/doc/general/features.html
capability: MSP | yes | https://github.com/nickgammon/mushclient/blob/master/plugins/msp.xml
capability: scripting | yes | https://www.mushclient.com/mushclient/doc/general/features.html
see-also: clients/mudlet
see-also: clients/potato
see-also: protocols/mccp
---

MUSHclient 是 Nick Gammon 的 Windows 客户端，MIT 许可，很长一段时间里是所有 Windows 用户的默认答
案。它可以用 Lua、VBScript、JScript、PerlScript 和 Python 写脚本，而它所做的很多事是由插件、而不是
由内核来承担的——这是一个货真价实的架构选择，也是上面好几行比看上去更难回答的原因。

最后一个打了标签的发布是 **5.06，2019 年 3 月**。仓库仍在被提交，也已经有了一个尚未发布的 5.07 的
发布说明。

## 为什么这么多行写着未知

它们每一个都是诚实答案为“我们无法确立”的情形，而理由各不相同：

- **GMCP**——项目自己关于它的页面给出的是一个你可以自己写的*示例*插件，而不是这个客户端具备的功
  能。那和随产品提供支持是两回事，所以那个格子写的是未知，而不是有。
- **TLS**——有文档的办法是外挂一个 `stunnel` 进程。一个加入 OpenSSL 支撑的 TLS 的提交在 2026 年落进
  了 master 分支，但不在任何一个发布版里，所以今天用户装得到的东西里，没有我们指得出来的。
- **UTF-8**——CHARSET 协商出现在尚未发布的 5.07 说明里，而在任何一个已发布版本的文档里，我们都没能
  找到它。
- **MSDP**——无论有无都没有说法。
- **屏幕阅读器**——客户端随附一个用 Windows SAPI 的文本转语音插件，而那和屏幕阅读器支持不是一回
  事。手册里没有无障碍章节，而作者本人在自己的论坛上说明过输出窗口为什么对阅读器不好用：它没有“当
  前行”这个概念。我们无法确立一个答案，所以这张表也不给出一个。

这些没有一个是*否*。其中好几项很可能是有，只是我们没能证明。
