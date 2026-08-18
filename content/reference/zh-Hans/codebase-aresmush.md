---
kind: codebase
slug: aresmush
title: AresMUSH
summary: 一个用 Ruby 写成的现代扮演服务器，自带 Web 前端和场景工具，而不是靠 softcode 写出来的。
codebase: AresMUSH
home: https://aresmush.com/
see-also: collaborative-roleplay
see-also: codebases/pennmush
see-also: codebases/evennia
---

AresMUSH 是目前广泛使用的服务器中最新的一个，明确瞄准**协作扮演**，它所取的立场与它接续的
TinyMUSH 一脉不同。PennMUSH 游戏的场景系统、角色卡和工单队列，是当时在场的人用 softcode 一点点搭
出来的；Ares 则把这些直接作为功能提供，并且期待游戏的管理人员去配置它们，而不是去编写它们。

它自带一个 **Web 门户**——角色维基、场景日志、论坛以及游戏本身，全都可以从浏览器访问——对于一个人
们事后要读日志的题材来说，这是种类上的差别，而不只是程度上的差别。

配置写在 YAML 里；扩展是 Ruby 插件。玩家没有游戏内的编程语言可用，这就是它的取舍：绳子少了，被绳
子勒伤的机会也少了，而 MUSH 一脉赖以得名的那种即兴营造文化，同样少了。

## 从外面看是什么样

没有 MSSP。它会回应登录前的 `WHO`，而回应是一份**逐个玩家的列表**，不是一个光秃秃的数字，我们的解
析器按结构来数它。在我们实测的那个游戏上，没有协商任何 telnet 选项。

如果你要为一个新的扮演游戏在它和 PennMUSH 之间做选择，问题大致是：你想要一个由你配置的系统，还是
一个由你编写的系统。
