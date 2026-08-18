---
kind: protocol
slug: ttype
title: TTYPE and MTTS
summary: 客户端如何告诉服务器自己是什么、能做什么——包括在它愿意开口的时候，说出正有人在使用屏幕阅读器。
protocol: TTYPE
home: https://www.mudhalla.net/tintin/protocols/mtts/
see-also: protocols/charset
see-also: clients/tintin
see-also: clients/blightmud
---

TTYPE 是 telnet 选项 24，出自 RFC 1091：服务器问客户端它是什么终端，客户端作答。
历史上这个答案是 `VT100` 或 `ANSI`。

**MTTS**——Mud Terminal Type Standard——在它之上叠了一层约定。客户端回答三次：它的名字、
它的终端类型，然后是 `MTTS <bitmask>`，其中的各个比特位自述它具备哪些能力。256 色、真彩色、
UTF-8、MNES、走带外的 MSP——以及很值得注意的 **`MTTS_SCREEN_READER`**。

## 屏幕阅读器那一位

最后这一项值得停下来说一说，因为在这个圈子的协议栈里，只有这一处把无障碍当作一等的概念。

设置了这一位的客户端，是在告诉服务器有人正在使用屏幕阅读器；注意到这一点的服务器可以随之调整：
不再输出 ASCII 图画，去掉房间描述外面那圈装饰性的制表边框，改变表格的排版方式。
[TinTin++](/reference/clients/tintin) 和 [Blightmud](/reference/clients/blightmud) 都会声明它，
[Mudlet](/reference/clients/mudlet) 有一个对应的设置项。

至于某个具体的游戏是否真的据此做了什么，那是另一个问题，而且不是本站能实测的问题——
我们没法去问一台服务器，它会做出什么不一样的处理。

## 爬虫在这里该尽的本分

爬虫要通过 TTYPE 表明自己的身份，它也应该这么做。我们的爬虫就这么做，并附上一个说明页地址，
好让翻看日志的管理员能弄清是谁一直在连他们的游戏，以及该怎么让我们停下。
一个只回答 `ANSI`、别的什么都不说的爬虫，是设计上就匿名的，而这没有什么好理由。

## 我们实测的是什么

与我们协商了 TTYPE 的服务器。要注意，这是少数几个由*我们*作为被询问一方的选项之一，
所以这里的数字，数的是那些愿意开口来问的服务器。
