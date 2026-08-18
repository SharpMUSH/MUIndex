---
kind: codebase
slug: rom
title: ROM
summary: Merc 最有名的后代，也是九十年代很大一部分 MUD 所建立在其上的战斗引擎。
codebase: ROM
see-also: codebases/dikumud
see-also: codebases/smaug
see-also: protocols/mccp
---

ROM——*Rivers of MUD*——是 **Merc** 的一个衍生版本，而 Merc 本身又是 DikuMUD 的衍生版本，ROM 则是站
住了脚的那一个。它的战斗模型、它的技能与法术系统以及它的区域格式，是九十年代及其后极大量游戏的起
点，尤其是 ROM 2.4，是这个爱好里被派生得最多的源码之一。

和 Diku 一脉的其余部分一样，它带着原始致谢的要求，所以一个你无法从别处确定其血统的游戏，往往会在
它的登录画面上同时提到 Diku、Merc 和 ROM。

## 从外面看是什么样

在我们实测的那个游戏上有 MSSP、CHARSET 和 **MCCP2**。

ROM 是本项目用来坐实自己那个压缩 bug 的服务器。我们的探测协商了 MCCP2，服务器正确地开始压缩，而我
们所依赖的 telnet 库始终没有解压这条流——于是连接画面到达时是一整片替换字符，我们一度把它记成了那
个游戏的问题。用一个现成的 zlib 调用，那段负载能干净地解压出来，正是这一点让事情不再含糊。它已在
上游修复；故事写在 [MCCP](/reference/protocols/mccp) 页上，因为它是一个从外面看起来和坏掉的游戏一
模一样的缺陷的好例子。
