---
kind: codebase
slug: tinymush
title: TinyMUSH
summary: MUSH 一脉的祖先，至今仍有游戏在跑。它让这个爬虫明白了：自己发出的协商字节，会毁掉它接下来发送的那条命令。
codebase: TinyMUSH
home: https://github.com/TinyMUSH/TinyMUSH
see-also: codebases/pennmush
see-also: codebases/tinymux
see-also: mush-mud-muck-moo
---

PennMUSH、TinyMUX、RhostMUSH 和 CobraMUSH 这一脉全都出自 TinyMUSH，而它至今仍有部署。它的开发是安
静，而不是不存在。

## 从外面看是什么样

没有 MSSP。登录前的 `WHO` 会用一句这种形式的话作答：
`0 Players logged in, 22 record, no maximum.`

## 它在我们身上找出的那个 bug

TinyMUSH 值得在这里占一段，因为正是这个游戏暴露了本站自己爬虫里的一个缺陷，而这次订正很好地说明了
“实测”该是什么意思。

我们的探测有好几周把 TinyMUSH 读成了*计数未知*。当时归档的猜测是它的回应没有尾随换行。它有。从线
路上抓下来看，真正的原因在我们这边：**TinyMUSH 在它的登录画面上不解析 telnet**，所以我们连接时发出
的 `IAC DO MSSP` 那三个字节，落进它的输入缓冲区，就像有人把它们敲了进去一样。它读到的下一行不是
`WHO`，而是三个控制字节后面跟着 `WHO`，那不是它认识的命令——于是它重新显示自己的连接画面，对玩家人
数只字不提。

现在探测会在协商之后发一个光秃秃的换行，并把它引出的任何东西丢弃，因为那份输出是对*我们*选择发送
的字节的反应，因此既不是游戏的连接画面，也不是它的回答。TinyMUSH 现在读得正确了，探测耗时也只有原
来的三分之一。

一个没有去查的目录，会在这个游戏存在的全部时间里发布“此游戏不报告其玩家人数”，而那句话说的其实是
我们。
