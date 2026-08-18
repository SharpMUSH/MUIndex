---
kind: protocol
slug: pueblo
title: Pueblo
summary: 更早的那套“在 MUD 里用 HTML”的方案，出自同名的客户端。MUSH 那一侧的客户端仍在支持它，它也经常被人与 MXP 弄混。
protocol: PUEBLO
home: https://pueblo.sourceforge.net/
see-also: protocols/mxp
see-also: clients/beipmu
---

Pueblo 出自九十年代中期同名的那个客户端，它增强 MUD 文本的路子很直接：让服务器发 **HTML**，
让客户端把它渲染出来。服务器在连接时用一行文字宣告自己支持 Pueblo，客户端作出回应，
从此这条流里就可以携带标记了。

它传到这个圈子里 MUSH 那一侧的程度大于 MUD 那一侧，而支持它的 MUSH 服务器一般至今仍在支持。

## 它不是 MXP

[MXP](/reference/protocols/mxp) 是更晚的那套方案，也是实现得更广的那套。两者干的活类似，
彼此并不兼容，而把某个客户端的 Pueblo 支持读成 MXP 支持——或者反过来——
是编制客户端对照时最容易犯的一个错。本节的客户端页面正因如此把它们分开列；
当一个项目只记载了其中一个而没有记载另一个时，另一个就写*未知*。

## 我们实测的是什么

Pueblo 的握手不是通常意义上的 telnet 选项，所以我们能观测到的范围比那些协商式协议要窄；
这里的数字低，应当读作一句关于我们可见范围的话，而不是关于部署情况的话。
