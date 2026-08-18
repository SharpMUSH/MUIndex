---
kind: protocol
slug: msdp
title: MSDP
summary: Mud Server Data Protocol——和 GMCP 干同一件事，用的是一种紧凑的二进制编码，外加一套 GMCP 所没有的发现机制。
protocol: MSDP
home: https://www.mudhalla.net/tintin/protocols/msdp/
see-also: protocols/gmcp
see-also: clients/tintin
see-also: clients/blightmud
---

MSDP 是 telnet 选项 69，它解决的问题与 [GMCP](/reference/protocols/gmcp) 相同：
在文本之外发送结构化数据，好让客户端不必从字里行间去抠数字。

差别有两处。MSDP 的编码是**二进制且紧凑的**——变量和取值用单个控制字节来标记，而不是包进 JSON——
并且 MSDP 定义了一套**发现**用的对话：客户端可以用 `LIST` 索取 `COMMANDS`、`REPORTABLE_VARIABLES`
等等，从而被告知某个游戏支持些什么。GMCP 没有对等的东西，这就是 GMCP 客户端一般得逐个游戏去配置的原因。

实际上，GMCP 在采用率上赢了，而 MSDP 在那些实现过它的服务器和客户端里留存下来，常常与 GMCP 并存。

## 我们实测的是什么

一个游戏被算进这里，条件是它的服务器在我们观测到的一次握手中提供了 MSDP。和本节里的每一个数字一样，
那是一次正面的观测，而剩下的部分并不是它的反面——一个没被算进来的游戏，可能没有实现 MSDP，
也可能只是它的握手还没被我们读到过。
