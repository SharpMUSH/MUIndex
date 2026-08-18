---
kind: codebase
slug: coffeemud
title: CoffeeMUD
summary: 一个用 Java 写的 MUD 服务器，拥有我们探测过的一切当中最大的一份 MSSP 报告，协议面也宽得反常。
codebase: CoffeeMUD
home: https://www.coffeemud.net/
see-also: codebases/dikumud
see-also: protocols/mssp
---

CoffeeMUD 是一个 Java 编写的 MUD 服务器，功能面宽得反常——它自带 Web 服务器、邮件、论坛和一套庞大
的职业与技能系统，而且它是这个爱好里少数几个不是用 C 写的服务器之一。

它在积极维护中，按本目录这一部分的标准，这是值得大声说出来的一件事。

## 从外面看是什么样

有 MSSP 和 **MCCP2**，而且在我们试过的二十台服务器里，CoffeeMUD 是仅有的三台会同时回应*明文*
`MSSP-REQUEST` 形式的服务器之一——这种形式比那个 telnet 选项还早，如今偶尔还能见到。

它的 MSSP 报告是我们实测到的最大的一份：**47 个字段**，其中 `PORT` 为九个不同的端口分九次报出。这
不是格式错误。MSSP 变量本来就是列表，而一个把多值 `PORT` 压平成单个字符串的爬虫，会从
`"80" "23" "4201"` 造出整数 `80234201`——这个 bug 本项目发布过，也修复了，也正是这里的解析器自始至
终把取值保留为列表的原因。
