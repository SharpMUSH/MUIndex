---
kind: protocol
slug: atcp
title: ATCP
summary: GMCP 的前身。带外数据，载荷格式更松散，如今大体已被取代，仍有一直没把它删掉的服务器在继续协商它。
protocol: ATCP
see-also: protocols/gmcp
see-also: protocols/msdp
see-also: clients/mudlet
---

ATCP——Achaea Telnet Client Protocol——是 telnet 选项 200，在 MUD 文本之外另发一份结构化数据的想法，
最早就是在这里被大规模用起来的。服务器发出一个模块名和一份载荷，客户端把它分发出去。

它的载荷格式比 [GMCP](/reference/protocols/gmcp) 的 JSON 松散，这基本上就是 GMCP 取代它的原因。
如今支持 ATCP 的客户端，一般都把它标注为已废弃，并让你改用 GMCP。

## 它为什么还在

因为把它开着并不会弄坏什么。一台 2008 年实现了 ATCP、2014 年又加上 GMCP 的服务器，通常两个都还在协商；
而两个都支持的客户端，对方给哪个就用哪个。

对一个新的实现来说，没有理由选它。

## 我们实测的是什么

在我们观测到的握手中提供了 telnet 选项 200 的服务器。这里的数字低是意料之中的，它说的是年代，别的什么也不说。
