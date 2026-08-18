---
kind: client
slug: beipmu
title: BeipMU
summary: 一个瞄准这个爱好中 MUSH 那一边的 Windows 客户端，输出窗口有屏幕阅读器支持，走的是 Pueblo 而不是 MXP。
home: https://beipdev.github.io/BeipMU/
platform: Windows
capability: screen reader | yes | https://github.com/BeipDev/BeipMU/blob/master/Assets/Changes.txt
capability: TLS | yes | https://beipdev.github.io/BeipMU/
capability: UTF-8 | yes | https://beipdev.github.io/BeipMU/
capability: MCCP | unknown |
capability: GMCP | yes | https://github.com/BeipDev/BeipMU/blob/master/Documentation/GMCP.md
capability: MSDP | unknown |
capability: ATCP | unknown |
capability: MXP | unknown |
capability: MSP | unknown |
capability: scripting | yes | https://beipdev.github.io/BeipMU/
see-also: clients/mushclient
see-also: clients/potato
see-also: collaborative-roleplay
---

BeipMU 是一个 MIT 许可的 Windows 客户端，发布活跃，也是少数几个在设计时想的是 MUSH 式玩法而不是战
斗 MUD 的客户端之一——多个输入窗口、spawn 窗口，以及一个预期会遇到长段落的文本引擎。脚本默认是
JavaScript，也可以用其他 ActiveScript 引擎。

## 无障碍

输出窗口实现了 Windows 的 `IAccessible` 接口，这是有意加进去的，作为迈向视障玩家可用性的一步，另外
还有一个用于文本转语音的 **Speak** 触发动作。任何地方都没有点名某个具体的屏幕阅读器，文档里也没有
无障碍相关的章节。

如果你要去翻，有一点需要留神：项目自己的文档里有一页至今仍写着 BeipMU 无法使用语音合成。那一页过时
了——变更日志和维护者本人在 issue 里的留言都晚于它。

## 关于这个客户端的两个容易犯的错

**BeipMU 实现的是 MCMP，不是 MSP。**它们是两个名字相似、用途也相似的不同协议，把其中一个读成另一
个，会在这张表里写下一个谁也没有做过的主张。所以 MSP 那一行写的是未知。

**它支持的是 Pueblo，不是 MXP。**Pueblo 是较早的那套“在 MUD 里用 HTML”的方案，MXP 是较晚的那套；
BeipMU 有文档说明基本的 Pueblo 样式和可点击链接。MXP 则无论有无都未能确立。
