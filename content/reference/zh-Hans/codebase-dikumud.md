---
kind: codebase
slug: dikumud
title: DikuMUD
summary: 战斗类 MUD 家族的根。等级、职业、装备和区域文件——以及一份塑造了整整一代衍生代码库的许可协议。
codebase: DikuMUD
home: https://dikumud.com/
see-also: codebases/circlemud
see-also: codebases/rom
see-also: codebases/smaug
see-also: mush-mud-muck-moo
---

DikuMUD 写于哥本哈根大学的 Datalogisk Institut，1991 年发布，人们不加限定地说“MUD”时所指的东西，
大多以它为祖先。等级、角色职业、生命值、mob、装备栏位、一种由建造者离线编写的区域文件格式——整套
词汇都出自这里，而那些从未见过 Diku 源码的游戏，依然继承了它的形状。

它的许可协议也是故事的一部分。Diku 可以免费使用，但禁止对访问收费，并要求显示原始致谢，正是这一条
款，使得“Diku 致谢”会出现在与它隔了好几次分支的游戏的登录画面上。

它的直系后代——**Merc**，然后是 **ROM**、**CircleMUD**、**SMAUG**、**tbaMUD** 以及其他几十个——在有
史以来的每一份 MUD 名录里都占了很大一部分。

## 从外面看是什么样

Diku 家族就是 **MSSP** 家族。MUSH 那一边通过登录画面上的 `WHO` 发布人数，完全不提供 MSSP；而 Diku
一脉的服务器绝大多数会用一份结构化报告回应 telnet 选项 70，本站上它们的数字正是从那里来的。

**MCCP2**——流压缩——在这个家族里也很常见，而且值得知道的是：一个协商了它、却无法解压这条流的客户
端，收到的整个连接画面会是一堆二进制噪声。这曾是本项目所依赖的 telnet 库里一个货真价实的缺陷，现
已修复；见 [MCCP](/reference/protocols/mccp)。
