---
kind: protocol
slug: mxp
title: MXP
summary: MUD eXtension Protocol——嵌在文本流里的类 HTML 标记，带来可点击的链接、图片和表单。规范写得详尽，实现却参差不齐。
protocol: MXP
home: https://www.zuggsoft.com/zmud/mxp.htm
see-also: protocols/pueblo
see-also: clients/mushclient
see-also: clients/mudlet
---

MXP 在服务器发出的文本里嵌入了一门小巧的、类似 HTML 的标记语言：`<send>` 表示一条可点击的命令，
`<a href>` 表示一个链接，另有颜色和字体元素，以及一套让服务器自定义标签的机制。
它在 telnet 选项 91 上协商。

它的设计难题是固有的，也很有意思：标记与文本走在同一条流里，所以服务器必须小心那些*看起来*像标记的文本，
而客户端必须小心自己会渲染什么。MXP 定义安全级别正是为了这个原因——
夹在另一个玩家的一行聊天里送来的标签，和服务器自己发出的标签不是一回事。

## 人们想要它，是为了可点击

MXP 实际用途的大部分，就是把 `north` 和物品名变成可以点的东西。对新玩家来说这个差别相当大，
也正因如此，这个协议尽管复杂，却还是不断有人实现。

## Pueblo 是另外那一个

[Pueblo](/reference/protocols/pueblo) 比 MXP 更早，用一种不同的、字面上更像 HTML 的做法干着类似的活。
支持其中一个的客户端往往并不支持另一个，而在读功能清单时这两者很容易混淆——
本节里的客户端对照表就得小心提防这个错误。

## 我们实测的是什么

在我们观测到的握手中提供了 telnet 选项 91 的服务器。MXP 被协商的频率低于那些带外协议，
一部分原因是它的价值有很大一块，是由那些干脆不协商、直接把标记发出去碰运气的服务器实现的——
而那些我们看不见。
