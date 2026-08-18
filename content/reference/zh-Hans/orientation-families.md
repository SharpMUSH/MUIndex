---
kind: orientation
slug: mush-mud-muck-moo
title: MUSH、MUD、MUCK、MOO——这些词是什么意思
summary: 四个词对应四种传统，没有一个是题材类型。它们真正告诉你的是什么。
see-also: collaborative-roleplay
see-also: connecting
see-also: codebases/pennmush
see-also: codebases/aresmush
see-also: codebases/muck
see-also: codebases/moo
see-also: codebases/evennia
---

这几个词，每一个命名的都是**一族服务器软件**，而不是一类游戏。这是关于它们最有用的一条认识，
也正是“这是 MUSH 还是 MUD？”这个问题为什么老被答得很糟：
老实的答案通常是*两者都是，而你想问的其实是文化*。

## MUD

最老的一个词，如今也是最宽的一个。它起初是 *Multi-User Dungeon*——Bartle 和 Trubshaw
1978 年的那个游戏——到九十年代中期，它已经成了所有文字多人世界的统称。

狭义地用，它指的是 **DikuMUD 和 LPMud 这两脉**：围绕等级、战斗、装备，
以及一个由建造者事先写好、描述各个房间的区域文件搭起来的服务器。
如果有人说“我玩 MUD”并且是有所指的，通常指的就是这个。

列表可以把每一脉单独显示给你看：[DikuMUD 的游戏](/games?lineage=DikuMUD)和
[LPMud 的游戏](/games?lineage=LPMud)。

## MUSH

*Multi-User Shared Hallucination*，出自 TinyMUD 一脉。它的定义性特征不是题材，
而是 **softcode（软代码）**：MUSH 服务器自带一门玩家在游戏内部使用的编程语言，
于是有建造权限的玩家可以创建房间、物件和行为，既不用碰源文件，也不用重启任何东西。

正是这一个设计决定造就了那套文化。MUSH 往往自动化系统很稀薄，而人力系统很密集——
管理组主持的剧情、写出来的场景、申请流程——因为玩的人同时也是造的人。

PennMUSH、TinyMUSH、TinyMUX、RhostMUSH、CobraMUSH 和 AresMUSH 都属于这一脉，
而它们没有一个这么说：MSSP 里没有 `MUSH` 这个取值可供公布，
而除 PennMUSH 之外的全都根本不发布 MSSP。所以把它们归到一起是我们做的事，
而不是我们读到的事，这就是[MUSH 的游戏](/games?lineage=MUSH)无论出现在哪里都标着*推算*的原因。

## MUCK

和 MUSH 一样是 TinyMUD 的后裔，有自己的 softcode（MUF，一门类 Forth 的语言），
以及一条深厚的社交世界与福瑞同人世界的传统。技术上与 MUSH 很接近；
文化上则区别到，两边都玩的人不会把它们说成是一回事——[MUCK 的游戏](/games?lineage=MUCK)。

## MOO

*MUD, Object-Oriented*。“游戏自己编辑自己”这个想法最纯粹的表达：MOO 里几乎所有东西，
都是使用它的人在里面用 MOO 编程语言写出来的。LambdaMOO 是它的祖先，
而 MOO 历来在教育和研究领域和在游戏领域一样受欢迎。[MOO 的游戏](/games?lineage=MOO)。

## 那么，你实际上该问什么？

有三个问题，比那个四字母的词管用得多：

1. **有没有战斗，战斗是不是自动化的？** 这一条把 Diku/LP 一脉和 TinyMUD 一脉分开，
   比任何名字都可靠。
2. **谁来建造？** 只有管理组，还是任何拿到建造位的人？
3. **玩法是约时间的还是随时随地的？** 是约好时间的场景和 pose 出来的扮演，还是登录就走？

本站的列表能替你回答第一个问题的一部分：我们为一个游戏实测出的**代码库**，
会告诉你它的服务器出自哪一种传统，而**谱系**这个筛选项就是把那个答案变得可筛选。
它没法告诉你文化，本页也不会假装它能。

关于这个筛选项还有一句提醒，因为总有人是在本页上第一次遇到它。代码库是实测的，谱系不是：
它是*我们*对一个游戏告诉我们的内容所作的归类，挂在它自己的标签下——**推算**——
与*实测*和*自述*并列。凡是代码库没有一个无争议的父系的，
我们宁可把它排除在所有谱系之外，也不把它归到最接近的那一个下面；
而这样的游戏里有好几个用它们自己的话表示了同意，公布的是 `FAMILY Custom`。
