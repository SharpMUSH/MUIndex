---
kind: protocol
slug: gmcp
title: GMCP
summary: Generic Mud Communication Protocol——与文本并行的结构化 JSON 消息，也是当下多数客户端所面向的带外通道。
protocol: GMCP
home: https://www.mudhalla.net/tintin/protocols/gmcp/
see-also: protocols/msdp
see-also: protocols/atcp
see-also: clients/mudlet
---

GMCP 是 telnet 选项 201。一旦协商成功，服务器就可以**带外发送结构化数据**：
一个包名加一份 JSON 载荷，与文本走在同一条流里，却不是文本的一部分。

`Char.Vitals { "hp": 412, "maxhp": 500 }` 是最典型的例子。客户端可以据此驱动一条血条，
而不必从字里行间去抠数字，这正是它的全部意义所在——靠模式匹配文本搭起来的状态显示，
在游戏改动提示符的那一天就会坏掉，建在 GMCP 上的则不会。

包的命名空间是约定俗成的，而不是标准化的。`Char`、`Room`、`Comm` 和 `Client` 用得很广；
再往外，各游戏需要什么就自己发明什么，而客户端一般得有人告诉它某个游戏会发些什么。

## 它为什么取代了 ATCP

GMCP 是 [ATCP](/reference/protocols/atcp) 的后继者，后者干的是同一件事，只是载荷格式更松散。
JSON 就是那个改进，而这场迁移到 2010 年代中期基本已经完成。一个游戏两个都支持并不稀奇；
一个新游戏只支持 ATCP 才叫稀奇。

## 我们实测的是什么

一个游戏被算进这里，条件是**它的服务器在我们观测到的一次握手中提供了 GMCP**。这与某个游戏的 MSSP 里写着
`GMCP 1` 是两种不同的说法——而这个圈子里大多数协议表恰恰是建立在后者之上的——两者经常对不上。

有一条来自我们自身历史的实测说明：有那么一段时间，凡是同时协商了
[MCCP](/reference/protocols/mccp) 的服务器，我们都看不到它们的 GMCP，
因为我们的 telnet 库协商了压缩却从不解压，压缩标记之后的一切对我们来说都是噪声。
我们调查过的服务器里，至少有一台其实一直都在说 GMCP。如果本页上的某个数字，
对一个你很熟悉的家族来说低得不对劲，第一个该怀疑的就是这类缺陷——怀疑我们，而不是怀疑他们。
